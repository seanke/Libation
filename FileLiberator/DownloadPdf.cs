﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DataLayer;
using Dinah.Core.ErrorHandling;
using Dinah.Core.Net.Http;
using FileManager;

namespace FileLiberator
{
	public class DownloadPdf : IProcessable
	{
		public event EventHandler<LibraryBook> Begin;
		public event EventHandler<LibraryBook> Completed;

		public event EventHandler<string> StreamingBegin;
		public event EventHandler<DownloadProgress> StreamingProgressChanged;
		public event EventHandler<string> StreamingCompleted;

		public event EventHandler<string> StatusUpdate;
		public event EventHandler<TimeSpan> StreamingTimeRemaining;

		public bool Validate(LibraryBook libraryBook)
			=> !string.IsNullOrWhiteSpace(getdownloadUrl(libraryBook))
			&& !libraryBook.Book.PDF_Exists;

		public async Task<StatusHandler> ProcessAsync(LibraryBook libraryBook)
		{
			Begin?.Invoke(this, libraryBook);

			try
			{
				var proposedDownloadFilePath = getProposedDownloadFilePath(libraryBook);
				var actualDownloadedFilePath = await downloadPdfAsync(libraryBook, proposedDownloadFilePath);
				var result = verifyDownload(actualDownloadedFilePath);

				libraryBook.Book.UserDefinedItem.PdfStatus = result.IsSuccess ? LiberatedStatus.Liberated : LiberatedStatus.NotLiberated;

				return result;
			}
			finally
			{
				Completed?.Invoke(this, libraryBook);
			}
		}

		private static string getProposedDownloadFilePath(LibraryBook libraryBook)
		{
			// if audio file exists, get it's dir. else return base Book dir
			var existingPath = Path.GetDirectoryName(AudibleFileStorage.Audio.GetPath(libraryBook.Book.AudibleProductId));
			var file = getdownloadUrl(libraryBook);

			if (existingPath != null)
				return Path.Combine(existingPath, Path.GetFileName(file));

			var full = FileUtility.GetValidFilename(
				AudibleFileStorage.PdfStorageDirectory,
				libraryBook.Book.Title,
				Path.GetExtension(file),
				libraryBook.Book.AudibleProductId);
			return full;
		}

		private static string getdownloadUrl(LibraryBook libraryBook)
			=> libraryBook?.Book?.Supplements?.FirstOrDefault()?.Url;

		private async Task<string> downloadPdfAsync(LibraryBook libraryBook, string proposedDownloadFilePath)
		{
			StreamingBegin?.Invoke(this, proposedDownloadFilePath);

			try
			{
				var api = await libraryBook.GetApiAsync();
				var downloadUrl = await api.GetPdfDownloadLinkAsync(libraryBook.Book.AudibleProductId);

				var progress = new Progress<DownloadProgress>();
				progress.ProgressChanged += (_, e) => StreamingProgressChanged?.Invoke(this, e);

				var client = new HttpClient();

				var actualDownloadedFilePath = await client.DownloadFileAsync(downloadUrl, proposedDownloadFilePath, progress);
				StatusUpdate?.Invoke(this, actualDownloadedFilePath);
				return actualDownloadedFilePath;
			}
			finally
			{
				StreamingCompleted?.Invoke(this, proposedDownloadFilePath);
			}
		}

		private static StatusHandler verifyDownload(string actualDownloadedFilePath)
			=> !File.Exists(actualDownloadedFilePath)
			? new StatusHandler { "Downloaded PDF cannot be found" }
			: new StatusHandler();
	}
}
