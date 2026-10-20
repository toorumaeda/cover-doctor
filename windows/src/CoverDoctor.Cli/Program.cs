using CoverDoctor.Core;

// 開発用のコマンド版（Linux / macOS / Windows）。使い方は CoverDoctor.Core/Cli.cs
Console.OutputEncoding = System.Text.Encoding.UTF8;
return Cli.Run(args, Console.WriteLine);
