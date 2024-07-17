﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using Octokit;

using Newtonsoft.Json;

using System.Reflection.Metadata.Ecma335;
using System.Text.Json.Serialization;
using System.Text.Json;
using ValveKeyValue;
using System.Net.NetworkInformation;

class Program
{
    private static readonly string GitHubUrl = "https://github.com/hayabusa255/SCPSLTranslationJP/releases/latest";
    private static readonly string DownloadUrlTemplate = "https://github.com/hayabusa255/SCPSLTranslationJP/releases/download/{0}/{1}.zip";

    private static readonly GitHubClient GitHubClient = new(new ProductHeaderValue("CsharpApp"));


    static async Task Main(string[] args)
    {

        if (!IsAdministrator())
        {

            try
            {


                Process.Start(new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    UseShellExecute = true,
                    Verb = "RunAs",
                });


                Environment.Exit(0);
            } catch (Exception ex)
            {
                Log.Error("管理者権限での実行が必要です。アプリケーションを再起動し、管理者として実行してください。 キーを押して終了します。");
                Console.ReadKey();
                Environment.Exit(1);
            }

        }
           
        
        try
        {
            // 最新のリリースタグを取得
            string latestTag = await GetLatestReleaseTag();
            if (string.IsNullOrEmpty(latestTag))
            {
                Log.Error("最新バージョンのタグが取得できませんでした。");
                return;
            }
            if(Directory.Exists(GetDirectoryAsync().Result) == false)
            {
                Log.Error("SCP: Secret Laboratoryがインストールされていません。インストール後もう一度実行してください。\nキーを押して終了してください。");
                Console.ReadKey();
                return;
            }
            if (Directory.Exists(Path.Combine(GetDirectoryAsync().Result, latestTag)))
            {
                Log.Info($"すでに最新版の日本語化ファイルがインストールされています。キーを押して終了してください。");
                Console.ReadKey();
            }
            else
            {

                DeleteOldTranslations(latestTag);

                string downloadUrl = string.Format(DownloadUrlTemplate, latestTag, latestTag);


                string zipFilePath = Path.Combine(Path.GetTempPath(), $"{latestTag}.zip");

                Log.Info($"{latestTag}.zipをダウンロードします。");
                await DownloadFileAsync(downloadUrl, zipFilePath);


                string extractPath = Path.Combine(GetDirectoryAsync().Result, latestTag);



                if (!Directory.Exists(extractPath))
                {
                    Directory.CreateDirectory(extractPath);
                }
                Log.Info($"{latestTag}.zipを{extractPath}に展開します。");
                // ZIPファイルを解凍
                await ExtractZipFileWithProgress(zipFilePath, extractPath);

                Log.Info("日本語化インストールが完了しました。キーを押して終了してください。");
                Console.ReadKey();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"エラーが発生しました: {ex.Message}");
        }
    }

    private static async Task<string> GetLatestReleaseTag()
    {
        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("User-Agent", "C# Application");
            HttpResponseMessage response = await client.GetAsync(GitHubUrl);
            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                string tag = ParseLatestReleaseTag(responseBody);
                return tag;
            }
        }
        return null;
    }
    public static bool IsAdministrator()
    {
        System.Security.Principal.WindowsIdentity identity
            = System.Security.Principal.WindowsIdentity.GetCurrent();
        System.Security.Principal.WindowsPrincipal principal
            = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
    private static string ParseLatestReleaseTag(string responseBody)
    {
        // HTMLから最新バージョンのタグ名をパースする（適宜修正が必要）
        // 例：<a href="/hayabusa255/SCPSLTranslationJP/releases/tag/v1.2.3"> の "v1.2.3" を抽出
        const string tagPrefix = "/hayabusa255/SCPSLTranslationJP/releases/tag/";
        int tagStartIndex = responseBody.IndexOf(tagPrefix) + tagPrefix.Length;
        int tagEndIndex = responseBody.IndexOf("\"", tagStartIndex);
        if (tagStartIndex > tagPrefix.Length && tagEndIndex > tagStartIndex)
        {
            return responseBody.Substring(tagStartIndex, tagEndIndex - tagStartIndex);
        }
        return null;
    }
    private static async Task<string> GetDirectoryAsync()
    {
        string steamPath = null;
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
            {
                if (key != null)
                {
                    steamPath = key.GetValue("InstallPath") as string;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error retrieving Steam path: {ex.Message}");
            return null;
        }
        try
        {

            string gamePath = SteamLibrary.GetLibraryFolders(steamPath);
          

            if (!string.IsNullOrEmpty(gamePath))
            {

                return gamePath + "/steamapps/common/SCP Secret Laboratory/Translations";
            }
            else
            {
                return null;
            }
        }
        catch (FileNotFoundException ex)
        {
            Log.Error(ex.Message);
            return null;
        };
        



    }

    private static async Task DownloadFileAsync(string url, string destinationPath)
    {
        using (HttpClient client = new HttpClient())
        {
            using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;

                using (Stream contentStream = await response.Content.ReadAsStreamAsync(),
                    fileStream = new FileStream(destinationPath, System.IO.FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    byte[] buffer = new byte[8192];
                    int bytesRead;
                    long totalBytesRead = 0;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;

                        if (totalBytes.HasValue)
                        {
                            Log.Info($"\rダウンロード中: {totalBytesRead} / {totalBytes} ({(totalBytesRead * 100 / totalBytes):0.00}%)");
                        }
                        else
                        {
                            Log.Info($"\rダウンロード中: {totalBytesRead} bytes");
                        }
                    }
                }
            }
        }
    }
    private static async Task ExtractZipFileWithProgress(string zipFilePath, string extractPath)
    {
        using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
        {
            long totalEntries = archive.Entries.Count;
            long currentEntry = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destinationPath = Path.Combine(extractPath, entry.FullName);
                if (string.IsNullOrEmpty(entry.Name))
                {
                    // フォルダの場合
                    Directory.CreateDirectory(destinationPath);
                }
                else
                {
                    // ファイルの場合
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    entry.ExtractToFile(destinationPath, true);
                }
                currentEntry++;
                Log.Info($"\r展開中: {currentEntry} / {totalEntries} ({(currentEntry * 100 / totalEntries):0.00}%)");
            }
        }
    }
    private static async void DeleteOldTranslations(string lastesttag)
    {
        var client = GitHubClient;
        var releases =  client.Repository.Release.GetAll("hayabusa255", "SCPSLTranslationJP").Result;
        var oldVersions = releases.Select(r => r.TagName).ToList();
        oldVersions.Remove(lastesttag);
        oldVersions.Add("jp");


        DirectoryInfo directoryInfo = new DirectoryInfo(GetDirectoryAsync().Result);
        foreach (DirectoryInfo dir in directoryInfo.GetDirectories().Where(d => oldVersions.Contains(d.Name)))
        {

                Log.Info($"古いバージョンの翻訳ファイル{dir.Name}が見つかりました。これを削除しますか？ (Y/N): ");
                var userInput = Console.ReadLine()?.ToString();
                if (userInput == "y" || userInput =="Y")
                {
                    try
                    {
                        dir.Delete(true);
                        Log.Info($"古いバージョンの翻訳ファイル '{dir.Name}' を削除しました。");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"古いバージョンの翻訳ファイル '{dir.Name}' を削除できませんでした: {ex.Message}");
                    }
                }
                else
                {
                    Log.Info("削除をスキップします。");
                }
            }
        }
    }

public class Log
{
    public static void Info(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] [Info] {text}");
        Console.ResetColor();
    }
    public static void Warn(string text)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] [Warning] {text}");
        Console.ResetColor();
    }
    public static void Error(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] [Error] {text}");
        Console.ResetColor();
        //エラーがコード何行目か取得
        StackTrace stackTrace = new StackTrace();
        StackFrame stackFrame = stackTrace.GetFrame(1);
        MethodBase methodBase = stackFrame.GetMethod();
        string methodName = methodBase.Name;
        int lineNumber = stackFrame.GetFileLineNumber();
        if (lineNumber != 0)
        {
            text += $" (at {methodName}() in {stackFrame.GetFileName()}:line {lineNumber})";
        }
        if(File.Exists("error.log"))
        {
            File.AppendAllText("error.log", $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] [Error] {text}\n");
        }
        else
        {
            File.WriteAllText("error.log", $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] [Error] {text}\n");
        }   

    } 
}
public class SteamLibrary
{
    public static string GetLibraryFolders(string steamPath)
    {
        string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (File.Exists(libraryFoldersPath))
        {

            try
            {
                var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
                using (var stream = File.OpenRead(libraryFoldersPath))
                {
                    var data = kv.Deserialize(stream);


                    foreach (var folder in data)
                    {
                        string path = (string)folder["path"];
                        if(File.Exists(Path.Combine(path, "steamapps", "appmanifest_700330.acf")))
                        {
                            return path;
                        }


                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"エラーが発生しました: {ex.Message}");
            }
        }
        return null;
    }

  


}



