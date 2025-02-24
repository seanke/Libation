﻿using AAXClean;
using Dinah.Core.IO;
using Dinah.Core.Net.Http;
using Dinah.Core.StepRunner;
using System;
using System.IO;

namespace AaxDecrypter
{
    public class AaxcDownloadConverter : AudiobookDownloadBase
    {
        protected override StepSequence steps { get; }

        private AaxFile aaxFile;

        private OutputFormat OutputFormat { get; }

        public AaxcDownloadConverter(string outFileName, string cacheDirectory, DownloadLicense dlLic, OutputFormat outputFormat)
            :base(outFileName, cacheDirectory, dlLic)
        {
            OutputFormat = outputFormat;

            steps = new StepSequence
            {
                Name = "Download and Convert Aaxc To " + OutputFormat,

                ["Step 1: Get Aaxc Metadata"] = Step1_GetMetadata,
                ["Step 2: Download Decrypted Audiobook"] = Step2_DownloadAudiobook,
                ["Step 3: Create Cue"] = Step3_CreateCue,
                ["Step 4: Cleanup"] = Step4_Cleanup,
            };
        }

        /// <summary>
        /// Setting cover art by this method will insert the art into the audiobook metadata
        /// </summary>
        public override void SetCoverArt(byte[] coverArt)
        {
            base.SetCoverArt(coverArt);

            aaxFile?.AppleTags.SetCoverArt(coverArt);
        }

        protected override bool Step1_GetMetadata()
        {    
            aaxFile = new AaxFile(InputFileStream);

            OnRetrievedTitle(aaxFile.AppleTags.TitleSansUnabridged);
            OnRetrievedAuthors(aaxFile.AppleTags.FirstAuthor ?? "[unknown]");
            OnRetrievedNarrators(aaxFile.AppleTags.Narrator ?? "[unknown]");
            OnRetrievedCoverArt(aaxFile.AppleTags.Cover);

            return !isCanceled;
        }

        protected override bool Step2_DownloadAudiobook()
        {
            var zeroProgress = new DownloadProgress 
            { 
                BytesReceived = 0,
                ProgressPercentage = 0, 
                TotalBytesToReceive = InputFileStream.Length 
            };

            OnDecryptProgressUpdate(zeroProgress);


            aaxFile.SetDecryptionKey(downloadLicense.AudibleKey, downloadLicense.AudibleIV);


            if (File.Exists(outputFileName))
                FileExt.SafeDelete(outputFileName);

            var outputFile =  File.Open(outputFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);

            aaxFile.ConversionProgressUpdate += AaxFile_ConversionProgressUpdate;
            var decryptionResult = OutputFormat == OutputFormat.M4b ? aaxFile.ConvertToMp4a(outputFile, downloadLicense.ChapterInfo) : aaxFile.ConvertToMp3(outputFile);
            aaxFile.ConversionProgressUpdate -= AaxFile_ConversionProgressUpdate;

            aaxFile.Close();

            downloadLicense.ChapterInfo = aaxFile.Chapters;

            CloseInputFileStream();

            OnDecryptProgressUpdate(zeroProgress);

            return decryptionResult == ConversionResult.NoErrorsDetected && !isCanceled;
        }

        private void AaxFile_ConversionProgressUpdate(object sender, ConversionProgressEventArgs e)
        {
            var duration = aaxFile.Duration;
            double remainingSecsToProcess = (duration - e.ProcessPosition).TotalSeconds;
            double estTimeRemaining = remainingSecsToProcess / e.ProcessSpeed;

            if (double.IsNormal(estTimeRemaining))
                OnDecryptTimeRemaining(TimeSpan.FromSeconds(estTimeRemaining));

            double progressPercent = e.ProcessPosition.TotalSeconds / duration.TotalSeconds;

            OnDecryptProgressUpdate(
                new DownloadProgress 
                {
                    ProgressPercentage = 100 * progressPercent,
                    BytesReceived = (long)(InputFileStream.Length * progressPercent),
                    TotalBytesToReceive = InputFileStream.Length
                });
        }

        public override void Cancel()
        {
            isCanceled = true;
            aaxFile?.Cancel();
            aaxFile?.Dispose();
            CloseInputFileStream();
        }

		protected override int GetSpeedup(TimeSpan elapsed)
            => (int)(aaxFile.Duration.TotalSeconds / (long)elapsed.TotalSeconds);
	}
}
