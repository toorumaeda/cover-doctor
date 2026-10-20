import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from collections import deque
SRC='base-1254.png'  # the original artwork (glitched / sunset pictures)
base=Image.open(SRC).convert('RGB'); A=np.asarray(base).astype(int)
H,W=A.shape[:2]
white0=(A.min(axis=2)>185)
white=np.asarray(Image.fromarray((white0*255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5)))>0
def picture_mask(box, badge):
    x0,y0,x1,y1=box
    bar=white.copy()
    yy,xx=np.mgrid[0:H,0:W]
    bar|=((xx-badge[0])**2+(yy-badge[1])**2)<=(badge[2]+3)**2
    sub=bar[y0:y1,x0:x1]; h,w=sub.shape
    seen=np.zeros_like(sub)
    q=deque()
    for x in range(w):
        for y in (0,h-1):
            if not sub[y,x]: seen[y,x]=1; q.append((y,x))
    for y in range(h):
        for x in (0,w-1):
            if not sub[y,x] and not seen[y,x]: seen[y,x]=1; q.append((y,x))
    while q:
        y,x=q.popleft()
        for dy,dx in ((1,0),(-1,0),(0,1),(0,-1)):
            ny,nx=y+dy,x+dx
            if 0<=ny<h and 0<=nx<w and not sub[ny,nx] and not seen[ny,nx]:
                seen[ny,nx]=1; q.append((ny,nx))
    pic=(~sub)&(~seen.astype(bool))
    m=np.zeros((H,W),bool); m[y0:y1,x0:x1]=pic
    return m
def quad_from_mask(m):
    ys,xs=np.nonzero(m); pts=np.stack([xs,ys],1).astype(float)
    c=pts.mean(0); best=None
    for deg in np.arange(-45,45,0.25):
        t=np.radians(deg); u=np.array([np.cos(t),np.sin(t)]); n=np.array([-u[1],u[0]])
        pu=(pts-c)@u; pn=(pts-c)@n
        a,b=np.percentile(pu,[0.1,99.9]); cc,d=np.percentile(pn,[0.1,99.9])
        area=(b-a)*(d-cc)
        if best is None or area<best[0]: best=(area,deg,u,n,a,b,cc,d)
    _,deg,u,n,a,b,cc,d=best
    print('angle',deg)
    return [c+a*u+cc*n, c+b*u+cc*n, c+b*u+d*n, c+a*u+d*n]
def coeffs(dst, src):
    # PIL wants mapping from output(dst) to input(src)
    M=[];B=[]
    for (x,y),(X,Y) in zip(dst,src):
        M.append([x,y,1,0,0,0,-X*x,-X*y]); B.append(X)
        M.append([0,0,0,x,y,1,-Y*x,-Y*y]); B.append(Y)
    return np.linalg.solve(np.array(M,float),np.array(B,float))
out=base.convert('RGBA')
for art,box,badge,hc in [('classical.png',(120,190,520,620),(180,295,66),(209,543)),('metal.png',(740,190,1150,620),(1070,292,68),(1037,569))]:
    m=picture_mask(box,badge)
    q=quad_from_mask(m)
    print(art,m.sum(),[tuple(np.round(p).astype(int)) for p in q])
    img=Image.open(art).convert('RGBA'); S=img.size[0]
    # expand quad slightly so art covers hidden corners
    cen=np.mean(q,0); qe=[cen+(p-cen)*1.03 for p in q]
    co=coeffs([tuple(p) for p in qe],[(0,0),(S,0),(S,S),(0,S)])
    warped=img.transform((W,H),Image.PERSPECTIVE,tuple(co),Image.BICUBIC)
    poly=Image.new('L',(W,H),0); ImageDraw.Draw(poly).polygon([tuple(p) for p in q],fill=255)
    P=np.asarray(poly)>0
    edge=P&~(np.asarray(poly.filter(ImageFilter.MinFilter(7)))>0)
    qy=np.mean([p[1] for p in q])
    polyx=Image.new('L',(W,H),0); ImageDraw.Draw(polyx).polygon([tuple(cen2+(p-cen2)*1.025) for cen2 in [np.mean(q,0)] for p in q],fill=255)
    ring=(np.asarray(polyx)>0)&~P&~(A.min(axis=2)>200)
    # hands: large near-white blobs touching the quad (pixels of the white frame outside the quad don't matter)
    wb=white0&P
    lab=np.zeros((H,W),int); keep=np.zeros((H,W),bool); cur=0
    ys,xs=np.nonzero(wb)
    for sy,sx in zip(ys,xs):
        if lab[sy,sx]: continue
        cur+=1; comp=[(sy,sx)]; lab[sy,sx]=cur; i=0
        while i<len(comp):
            y,x=comp[i]; i+=1
            for dy,dx in ((1,0),(-1,0),(0,1),(0,-1)):
                ny,nx=y+dy,x+dx
                if wb[ny,nx] and not lab[ny,nx]: lab[ny,nx]=cur; comp.append((ny,nx))
        cy,cx=np.mean(comp,0)
        if len(comp)>150 and cy>qy and any(edge[y,x] for y,x in comp):
            for y,x in comp: keep[y,x]=True
    hand=np.asarray(Image.fromarray((keep*255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5)))>0
    yy,xx=np.mgrid[0:H,0:W]
    bad=((xx-badge[0])**2+(yy-badge[1])**2)<=(badge[2]+2)**2
    fm=(P|ring)&~hand&~bad
    mk=Image.fromarray((fm*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.7))
    out.paste(warped,(0,0),mk)
out.convert('RGB').save('icon-1254.png')
