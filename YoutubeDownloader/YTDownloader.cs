﻿using MediaToolkit;
using MediaToolkit.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YoutubeExtractor;

namespace YoutubeDownloader
{
    public class YTDownloader
    {
        #region Settings
        private string ExportVideoDirPath { get; set; }
        private string ExportAudioDirPath { get; set; }
        private ExportOptions ExportOptions { get; set; }
        private bool SkipVideosWhichExists { get; set; }
        #endregion

        private IDictionary<Guid, Engine> engines;
        private IDictionary<Guid, VideoDownloader> videoDownloaders;

        private IList<Task<DownloadResult>> tasks;
        private IList<LinkInfo> linksToProcess;

        #region AudioEngineAPI
        public void AddAudioConvertProgressAction(Guid guid, Action<ConvertProgressEventArgs> action)
        {
            engines[guid].ConvertProgressEvent += (sender, args) => { action.Invoke(args); };
        }

        public void AddAudioConversionCompleteAction(Guid guid, Action<ConversionCompleteEventArgs> action)
        {
            engines[guid].ConversionCompleteEvent += (sender, args) => { action.Invoke(args); };
        }
        #endregion

        #region VideoDownloaderAPI
        public void AddDownloadProgressChangedAction(Guid guid, Action<ProgressEventArgs> action)
        {
            videoDownloaders[guid].DownloadProgressChanged += (sender, args) => { action.Invoke(args); };
        }

        public void AddDownloadStartedAction(Guid guid, Action<EventArgs> action)
        {
            videoDownloaders[guid].DownloadStarted += (sender, args) => { action.Invoke(args); };
        }

        public void AddDownloadFinishedAction(Guid guid, Action<EventArgs> action)
        {
            videoDownloaders[guid].DownloadFinished += (sender, args) => { action.Invoke(args); };
        }
        #endregion

        public YTDownloader(YTDownloaderBuilder builder, IList<LinkInfo> links)
        {
            Init(builder);

            foreach (var linkInfo in links)
            {
                engines.Add(linkInfo.GUID, new Engine());
                videoDownloaders.Add(linkInfo.GUID, new VideoDownloader());
                linksToProcess.Add(linkInfo);
            }
        }

        public YTDownloader(YTDownloaderBuilder builder, string [] urls)
        {
            Init(builder);

            foreach (var url in urls)
            {
                var linkInfo = new LinkInfo(url);

                engines.Add(linkInfo.GUID, new Engine());
                videoDownloaders.Add(linkInfo.GUID, new VideoDownloader());
                linksToProcess.Add(linkInfo);
            }
        }

        private void Init(YTDownloaderBuilder builder)
        {
            engines = new Dictionary<Guid, Engine>();
            videoDownloaders = new Dictionary<Guid, VideoDownloader>();

            tasks = new List<Task<DownloadResult>>();
            linksToProcess = new List<LinkInfo>();

            this.ExportAudioDirPath = builder.ExportAudioDirPath;
            this.ExportVideoDirPath = builder.ExportVideoDirPath;
            this.ExportOptions = builder.ExportOptions;
            this.SkipVideosWhichExists = builder.SkipVideosWhichExists;
        }

        public DownloadResult[] DownloadLinks()
        {
            return ProcessDownloads().Result;
        }

        public async Task<DownloadResult[]> DownloadLinksAsync()
        {
            return await ProcessDownloads();
        }

        private async Task<DownloadResult[]> ProcessDownloads()
        {
            foreach (var linkInfo in linksToProcess)
            {
                Task<DownloadResult> downloadTask = Task.Run(() =>
                {
                    string videoName;
                    string videoFilePath;

                    var videoInfo = GetVideoInfo(linkInfo, out videoName, out videoFilePath);

                    if (File.Exists(videoFilePath) && SkipVideosWhichExists)
                        return new DownloadResult() { VideoSavedFilePath = videoFilePath, GUID = linkInfo.GUID, AudioSavedFilePath = null, FileBaseName = videoName, DownloadSkipped = true };

                    DownloadVideo(videoInfo, linkInfo.GUID, videoFilePath);

                    if (ExportOptions.HasFlag(ExportOptions.ExportAudio))
                        DownloadAudio(linkInfo, videoFilePath, videoInfo.VideoExtension);

                    if (ExportOptions.HasFlag(ExportOptions.ExportAudio) && !ExportOptions.HasFlag(ExportOptions.ExportVideo))
                    {
                        File.Delete(videoFilePath);
                    }

                    return new DownloadResult() { VideoSavedFilePath = videoFilePath, GUID = linkInfo.GUID, AudioSavedFilePath = TransFormToAudioPath(videoFilePath, videoInfo.VideoExtension), FileBaseName = videoName, DownloadSkipped = false };
                });

                tasks.Add(downloadTask);
            }
            return await Task.WhenAll(tasks);
        }

        private VideoInfo GetVideoInfo(LinkInfo linkInfo, out string videoName, out string videoFilePath)
        {
            IEnumerable<VideoInfo> videoInfos = DownloadUrlResolver.GetDownloadUrls(linkInfo.URL, false);

            // Select the first video by type with highest AudioBitrate
            VideoInfo videoInfo = videoInfos
                .OrderByDescending(info => info.AudioBitrate)
                .First(info => info.VideoType == linkInfo.VideoType);

            // This is must, cause we decrypting only this video
            if (videoInfo.RequiresDecryption)
            {
                DownloadUrlResolver.DecryptDownloadUrl(videoInfo);
            }

            string fileBaseName = linkInfo.FileName == null ? videoInfo.Title.ToSafeFileName() : linkInfo.FileName.ToSafeFileName();

            videoName = fileBaseName;
            videoFilePath = Path.Combine(ExportVideoDirPath, fileBaseName + videoInfo.VideoExtension);

            return videoInfo;
        }

        private void DownloadVideo(VideoInfo videoInfo, Guid linkGUID, string videoFilePath)
        {
            var videoDownloader = videoDownloaders[linkGUID];

            videoDownloader.Init(videoInfo, videoFilePath);
            videoDownloader.Execute();
        }

        private void DownloadAudio(LinkInfo linkInfo, string videoFilePath, string videoExtension)
        {
            string audioOutputPath = TransFormToAudioPath(videoFilePath, videoExtension);

            var inputFile = new MediaFile { Filename = videoFilePath };
            var outputFile = new MediaFile { Filename = audioOutputPath };

            var engine = engines[linkInfo.GUID];

            engine.Convert(inputFile, outputFile);
            engine.Dispose();
        }

        private string TransFormToAudioPath(string videoFilePath, string videoExtension)
        {
            string audioOutputPath = videoFilePath
                                    .Replace(ExportVideoDirPath, ExportAudioDirPath)
                                    .ReplaceLastOccurrence(videoExtension, ".mp3");
            return audioOutputPath;
        }
    }
}
