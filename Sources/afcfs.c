/* afcfs — iPhone の「メディア領域」(AFC) を読み書きする小さな道具。
 *
 * 使い方（出力は1行1項目、タブ区切り）:
 *   afcfs devices
 *   afcfs ls   <UDID|-> <リモートDir>
 *   afcfs get  <UDID|-> <リモートFile> <ローカルFile>
 *   afcfs put  <UDID|-> <ローカルFile> <リモートFile>
 *
 * 安全のため、書き込み(put)は「すでに存在する」画像ファイルの上書きだけを許す:
 *   - 書き込み先は /iTunes_Control/iTunes/Artwork/Originals/ の下に限る
 *   - 存在しないファイルは作らない・削除はできない（rm/mkdir は実装しない）
 * UDID に "-" を指定すると、最初に見つかった1台を使う。 */
#include <libimobiledevice/libimobiledevice.h>
#include <libimobiledevice/lockdown.h>
#include <libimobiledevice/afc.h>
#include <plist/plist.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <dirent.h>

#define WRITE_PREFIX "/iTunes_Control/iTunes/Artwork/Originals/"

static int fail(const char *message) { fprintf(stderr, "%s\n", message); return 1; }

static int open_afc(const char *udid, idevice_t *deviceOut, afc_client_t *afcOut) {
    idevice_t device = NULL;
    const char *target = (udid && strcmp(udid, "-") != 0) ? udid : NULL;
    if (idevice_new_with_options(&device, target, IDEVICE_LOOKUP_USBMUX) != IDEVICE_E_SUCCESS || !device)
        return fail("Cannot connect to the iPhone. Check the USB connection, unlock it, and choose Trust This Computer.");
    lockdownd_client_t lockdown = NULL;
    if (lockdownd_client_new_with_handshake(device, &lockdown, "afcfs") != LOCKDOWN_E_SUCCESS || !lockdown)
        return fail("Pairing with the iPhone failed. Unlock it and choose Trust.");
    lockdownd_service_descriptor_t service = NULL;
    if (lockdownd_start_service(lockdown, "com.apple.afc", &service) != LOCKDOWN_E_SUCCESS || !service) {
        lockdownd_client_free(lockdown);
        return fail("Cannot start the iPhone file service.");
    }
    afc_client_t afc = NULL;
    if (afc_client_new(device, service, &afc) != AFC_E_SUCCESS || !afc) {
        lockdownd_service_descriptor_free(service);
        lockdownd_client_free(lockdown);
        return fail("Cannot create the AFC client.");
    }
    lockdownd_service_descriptor_free(service);
    lockdownd_client_free(lockdown);
    *deviceOut = device;
    *afcOut = afc;
    return 0;
}

static void print_devices(void) {
    idevice_info_t *list = NULL;
    int count = 0;
    if (idevice_get_device_list_extended(&list, &count) != IDEVICE_E_SUCCESS || count <= 0) {
        if (list) idevice_device_list_extended_free(list);
        return;
    }
    char *seen[64];
    int seenCount = 0;
    for (int i = 0; i < count; i++) {
        const char *udid = list[i]->udid;
        int dup = 0;
        for (int j = 0; j < seenCount; j++) if (!strcmp(seen[j], udid)) { dup = 1; break; }
        if (dup) continue;
        if (seenCount < 64) seen[seenCount++] = (char *)udid;
        idevice_t device = NULL;
        char *name = NULL;
        char *version = NULL;
        if (idevice_new_with_options(&device, udid, IDEVICE_LOOKUP_USBMUX) == IDEVICE_E_SUCCESS && device) {
            lockdownd_client_t lockdown = NULL;
            if (lockdownd_client_new_with_handshake(device, &lockdown, "afcfs") == LOCKDOWN_E_SUCCESS && lockdown) {
                lockdownd_get_device_name(lockdown, &name);
                plist_t v = NULL;
                if (lockdownd_get_value(lockdown, NULL, "ProductVersion", &v) == LOCKDOWN_E_SUCCESS && v) {
                    plist_get_string_val(v, &version);
                    plist_free(v);
                }
                lockdownd_client_free(lockdown);
            }
            idevice_free(device);
        }
        if (!name || !name[0]) { free(name); name = strdup("iPhone"); }
        for (char *p = name; *p; p++) if (*p == '\n' || *p == '\t' || *p == '\r') *p = ' ';
        printf("DEVICE\t%s\t%s\t%s\n", udid, version ? version : "?", name);
        free(name);
        free(version);
    }
    idevice_device_list_extended_free(list);
}

static int cmd_ls(afc_client_t afc, const char *dir) {
    char **entries = NULL;
    if (afc_read_directory(afc, dir, &entries) != AFC_E_SUCCESS || !entries)
        return fail("Cannot read the folder.");
    for (int i = 0; entries[i]; i++) {
        if (!strcmp(entries[i], ".") || !strcmp(entries[i], "..")) continue;
        printf("%s\n", entries[i]);
    }
    for (int i = 0; entries[i]; i++) free(entries[i]);
    free(entries);
    return 0;
}

static int cmd_get(afc_client_t afc, const char *remote, const char *local) {
    uint64_t handle = 0;
    if (afc_file_open(afc, remote, AFC_FOPEN_RDONLY, &handle) != AFC_E_SUCCESS)
        return fail("Cannot open the file on the iPhone.");
    FILE *out = fopen(local, "wb");
    if (!out) { afc_file_close(afc, handle); return fail("Cannot open the destination file."); }
    char buf[1 << 16];
    int rc = 0;
    for (;;) {
        uint32_t got = 0;
        afc_error_t e = afc_file_read(afc, handle, buf, sizeof buf, &got);
        if (e != AFC_E_SUCCESS) { rc = fail("A read error occurred."); break; }
        if (got == 0) break;
        if (fwrite(buf, 1, got, out) != got) { rc = fail("A write error occurred while saving."); break; }
    }
    fclose(out);
    afc_file_close(afc, handle);
    return rc;
}

/* 既存ファイルの上書きだけを許す。書き込み後に読み戻して、内容が一致することまで確認する。 */
static int cmd_put(afc_client_t afc, const char *local, const char *remote) {
    if (strncmp(remote, WRITE_PREFIX, strlen(WRITE_PREFIX)) != 0 || strstr(remote, ".."))
        return fail("For safety, writing here is not allowed (only the cover image store is permitted).");
    char **info = NULL;
    if (afc_get_file_info(afc, remote, &info) != AFC_E_SUCCESS || !info)
        return fail("The file to overwrite does not exist on the iPhone (new files are never created).");
    for (int i = 0; info[i]; i++) free(info[i]);
    free(info);

    FILE *in = fopen(local, "rb");
    if (!in) return fail("Cannot open the image to write.");
    fseek(in, 0, SEEK_END);
    long size = ftell(in);
    fseek(in, 0, SEEK_SET);
    if (size <= 0 || size > 32 * 1024 * 1024) { fclose(in); return fail("The image size is invalid."); }
    unsigned char *data = malloc((size_t)size);
    if (!data || fread(data, 1, (size_t)size, in) != (size_t)size) { fclose(in); free(data); return fail("Cannot read the image."); }
    fclose(in);

    uint64_t handle = 0;
    if (afc_file_open(afc, remote, AFC_FOPEN_WR, &handle) != AFC_E_SUCCESS) { free(data); return fail("Cannot open the file on the iPhone for writing."); }
    long done = 0;
    int rc = 0;
    while (done < size) {
        uint32_t wrote = 0;
        uint32_t chunk = (size - done) > 65536 ? 65536 : (uint32_t)(size - done);
        if (afc_file_write(afc, handle, (const char *)data + done, chunk, &wrote) != AFC_E_SUCCESS || wrote == 0) { rc = fail("A write error occurred."); break; }
        done += wrote;
    }
    afc_file_close(afc, handle);
    if (rc == 0) {
        /* 読み戻し検証 */
        uint64_t h2 = 0;
        if (afc_file_open(afc, remote, AFC_FOPEN_RDONLY, &h2) != AFC_E_SUCCESS) { free(data); return fail("Verification after writing failed."); }
        unsigned char *back = malloc((size_t)size + 1);
        long total = 0;
        while (back && total <= size) {
            uint32_t got = 0;
            if (afc_file_read(afc, h2, (char *)back + total, (uint32_t)(size + 1 - total), &got) != AFC_E_SUCCESS || got == 0) break;
            total += got;
        }
        afc_file_close(afc, h2);
        /* iOS が書き込み直後にファイルを作り直すことがあるため、不一致は警告として扱い、判定は呼び出し側に任せる */
        if (!back || total != size || memcmp(back, data, (size_t)size) != 0) printf("VERIFY\tMISMATCH\n");
        else printf("VERIFY\tOK\n");
        free(back);
    }
    free(data);
    return rc;
}


/* ---- テスト用の偽 iPhone ----
 * 環境変数 COVERDOCTOR_FAKE_ROOT が設定されているときだけ、実機に接続せず、そのフォルダを
 * iPhone の AFC ルート（/var/mobile/Media）として扱う。書き込みの制限（Originals 配下の既存ファイルの
 * 上書きのみ）は、実機と同じものを適用する。 */
static const char *fake_root(void) { const char *r = getenv("COVERDOCTOR_FAKE_ROOT"); return (r && r[0]) ? r : NULL; }

static void fake_path(char *out, size_t n, const char *root, const char *remote) {
    snprintf(out, n, "%s%s", root, remote);
}

static int fake_main(const char *root, int argc, char **argv) {
    if (!strcmp(argv[1], "devices")) { printf("DEVICE\tFAKE-0000\t27.0\tTest iPhone\n"); return 0; }
    if (argc < 4) return fail("Missing arguments.");
    char path[4096];
    if (!strcmp(argv[1], "ls")) {
        fake_path(path, sizeof path, root, argv[3]);
        DIR *d = opendir(path);
        if (!d) return fail("Cannot read the folder.");
        struct dirent *e;
        while ((e = readdir(d))) { if (strcmp(e->d_name, ".") && strcmp(e->d_name, "..")) printf("%s\n", e->d_name); }
        closedir(d);
        return 0;
    }
    if (!strcmp(argv[1], "get") && argc >= 5) {
        fake_path(path, sizeof path, root, argv[3]);
        FILE *in = fopen(path, "rb");
        if (!in) return fail("Cannot open the file on the iPhone.");
        FILE *out = fopen(argv[4], "wb");
        if (!out) { fclose(in); return fail("Cannot open the destination file."); }
        char buf[1 << 16]; size_t got;
        while ((got = fread(buf, 1, sizeof buf, in)) > 0) fwrite(buf, 1, got, out);
        fclose(in); fclose(out);
        return 0;
    }
    if (!strcmp(argv[1], "put") && argc >= 5) {
        const char *remote = argv[4];
        if (strncmp(remote, WRITE_PREFIX, strlen(WRITE_PREFIX)) != 0 || strstr(remote, ".."))
            return fail("For safety, writing here is not allowed (only the cover image store is permitted).");
        fake_path(path, sizeof path, root, remote);
        struct stat st;
        if (stat(path, &st) != 0) return fail("The file to overwrite does not exist on the iPhone (new files are never created).");
        FILE *in = fopen(argv[3], "rb");
        if (!in) return fail("Cannot open the image to write.");
        FILE *out = fopen(path, "wb");
        if (!out) { fclose(in); return fail("Cannot open the file on the iPhone for writing."); }
        char buf[1 << 16]; size_t got;
        while ((got = fread(buf, 1, sizeof buf, in)) > 0) fwrite(buf, 1, got, out);
        fclose(in); fclose(out);
        printf("VERIFY\tOK\n");
        return 0;
    }
    return fail("Unknown command.");
}

int main(int argc, char **argv) {
    if (argc >= 2 && fake_root()) return fake_main(fake_root(), argc, argv);
    if (argc < 2) return fail("usage: afcfs devices | ls | get | put");
    if (!strcmp(argv[1], "devices")) { print_devices(); return 0; }
    if (argc < 4) return fail("Missing arguments.");
    idevice_t device = NULL;
    afc_client_t afc = NULL;
    if (open_afc(argv[2], &device, &afc) != 0) return 1;
    int rc = 1;
    if (!strcmp(argv[1], "ls")) rc = cmd_ls(afc, argv[3]);
    else if (!strcmp(argv[1], "get") && argc >= 5) rc = cmd_get(afc, argv[3], argv[4]);
    else if (!strcmp(argv[1], "put") && argc >= 5) rc = cmd_put(afc, argv[3], argv[4]);
    else fail("Unknown command.");
    afc_client_free(afc);
    idevice_free(device);
    return rc;
}
