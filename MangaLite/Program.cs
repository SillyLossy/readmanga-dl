using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Flurl.Http;
using System.Threading;

namespace MangaLite
{
    class Program
    {
        public class ReadMangaDownloader
        {
            private readonly Uri url;

            private readonly List<Task> tasks = new List<Task>();

            private const string chaptersXPath = "//div[contains(@class, 'chapters-link')]//a";
            private const string nameXPath = "//span[contains(@class, 'name')]";

            public ReadMangaDownloader(string mangaUrl)
            {
                url = new Uri(mangaUrl);
            }

            private Uri GetChapterUrl(HtmlNode x) => new UriBuilder(url.Scheme, url.Host) { Path = x.Attributes["href"].Value }.Uri;

            public void DownloadManga()
            {
                var sw = new Stopwatch();
                sw.Start();

                var mangaDoc = new HtmlWeb().Load(url);
                string mangaName = mangaDoc.DocumentNode.SelectSingleNode(nameXPath).InnerText;
                var chapterNodes = mangaDoc.DocumentNode.SelectNodes(chaptersXPath);
                var chapterLinks = chapterNodes.Select(GetChapterUrl);

                foreach (var link in chapterLinks)
                {
                    DownloadChapter(link, mangaName);
                }

                Task.WhenAll(tasks).Wait();
                sw.Stop();
                Console.WriteLine($"Done in {new TimeSpan(sw.ElapsedTicks).TotalMinutes} minutes");
                CompressAllSubfolders(mangaName);
            }

            private void DownloadChapter(Uri link, string mangaName)
            {
                var folder = link.ToString().Split('/').Last();
                var chaptedDoc = Retry.Do(() => new HtmlWeb().Load(link), TimeSpan.FromSeconds(10));
                var chapterLines = chaptedDoc.Text.Split("\r\n").Select(x => x.Trim()).ToList();
                string initLine = chapterLines.Single(x => x.Contains("rm_h.init")).Replace(" ", "").Replace("rm_h.init(", "").Replace(",0,false);", "");
                string serversLine = chapterLines.Single(x => x.Contains("var servers")).Replace("var servers = ", "").Replace(";", "");
                var servers = ((JArray)JsonConvert.DeserializeObject(serversLine)).Select(x => new Uri(x.Value<string>())).ToList();
                var pageLinks = ((JArray)JsonConvert.DeserializeObject(initLine)).Select(x => x[1].Value<string>() + x[2].Value<string>()).ToList();
                pageLinks.ForEach(x => tasks.Add(DownloadPage(Path.Combine(mangaName, folder), x, servers)));
            }


            private void CompressAllSubfolders(string rootDirectory)
            {
                foreach (var directory in Directory.EnumerateDirectories(rootDirectory))
                {
                    CompressSubfolder(rootDirectory, directory);
                }
            }

            private void CompressSubfolder(string rootDirectory, string directory)
            {
                string zipPath = Path.Combine(rootDirectory, $"{Path.GetDirectoryName(directory)}.zip");
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                    }
                }
            }

            private async Task DownloadPage(string folder, string link, List<Uri> servers)
            {
                var url = new Uri(link);
                while (true)
                {
                    try
                    {
                        await url.ToString().DownloadFileAsync(folder);
                        return;
                    }
                    catch (Exception e)
                    {
                        url = new UriBuilder(url) { Host = servers.Random().Host }.Uri;
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            new ReadMangaDownloader("http://readmanga.me/it_s_not_my_fault_that_i_m_not_popular").DownloadManga();
        }
    }

    public static class ListExtensions
    {
        private static readonly Random random = new Random();

        public static T Random<T>(this IList<T> list)
        {
            return list[random.Next(list.Count)];
        }
    }

    public static class Retry
    {
        public static TOut Do<TOut>(Func<TOut> func, TimeSpan wait)
        {
            while (true)
            {
                try
                {
                    return func();
                }
                catch (Exception e)
                {
                    Thread.Sleep((int) wait.TotalMilliseconds);
                }
            }
        }
    }
}
