﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dinah.Core;

namespace DataLayer
{
    /// <summary>
    /// Do not track in-process state. In-process state is determined by the presence of temp file.
    /// </summary>
    public enum LiberatedStatus
    {
        NotLiberated = 0,
        Liberated = 1,
        /// <summary>Error occurred during liberation. Don't retry</summary>
        Error = 2,

        /// <summary>Application-state only. Not a valid persistence state.</summary>
        PartialDownload = 0x1000
    }

    public class UserDefinedItem
    {
        internal int BookId { get; private set; }
        public Book Book { get; private set; }

        private UserDefinedItem() { }
        internal UserDefinedItem(Book book)
		{
			ArgumentValidator.EnsureNotNull(book, nameof(book));
            Book = book;

			// import previously saved tags
			ArgumentValidator.EnsureNotNullOrWhiteSpace(book.AudibleProductId, nameof(book.AudibleProductId));
			Tags = FileManager.TagsPersistence.GetTags(book.AudibleProductId);
		}

        #region Tags
        private string _tags = "";
        public string Tags
        {
            get => _tags;
            set
            {
                var newTags = sanitize(value);
                if (_tags != newTags)
                {
                    _tags = newTags;
                    ItemChanged?.Invoke(this, nameof(Tags));
                }
            }
		}

		public IEnumerable<string> TagsEnumerated => Tags == "" ? new string[0] : Tags.Split(null as char[], StringSplitOptions.RemoveEmptyEntries);

		#region sanitize tags: space delimited. Inline/denormalized. Lower case. Alpha numeric and hyphen
		// only legal chars are letters numbers underscores and separating whitespace
		//
		// technically, the only char.s which aren't easily supported are  \  [  ]
		// however, whitelisting is far safer than blacklisting (eg: new lines, non-printable character)
		// it's easy to expand whitelist as needed
		// for lucene, ToLower() isn't needed because search is case-inspecific. for here, it prevents duplicates
		//
		// there are also other allowed but misleading characters. eg: the ^ operator defines a 'boost' score
		// full list of characters which must be escaped:
		//   + - && || ! ( ) { } [ ] ^ " ~ * ? : \
		static Regex regex { get; } = new Regex(@"[^\w\d\s_]", RegexOptions.Compiled);
        private static string sanitize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var str = input
                .Trim()
                .ToLowerInvariant()
                // assume a hyphen is supposed to be an underscore
                .Replace("-", "_");

            var unique = regex
                // turn illegal characters into a space. this will also take care of turning new lines into spaces
                .Replace(str, " ")
                // split and remove excess spaces
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                // de-dup
                .Distinct()
                // this will prevent order from being relevant
                .OrderBy(a => a);

            // currently, the string is the canonical set. if we later make the collection into the canonical set:
            //   var tags = new Hashset<string>(list); // de-dup, order doesn't matter but can seem random due to hashing algo
            //   var isEqual = tagsNew.SetEquals(tagsOld);

            return string.Join(" ", unique);
        }
        #endregion
        #endregion

        #region Rating
        // owned: not an optional one-to-one
        /// <summary>The user's individual book rating</summary>
        public Rating Rating { get; private set; } = new Rating(0, 0, 0);

        public void UpdateRating(float overallRating, float performanceRating, float storyRating)
            => Rating.Update(overallRating, performanceRating, storyRating);
        #endregion

        #region LiberatedStatuses
        /// <summary>
        /// Occurs when <see cref="Tags"/>, <see cref="BookStatus"/>, or <see cref="PdfStatus"/> values change.
        /// This signals the change of the in-memory value; it does not ensure that the new value has been persisted.
        /// </summary>
        public static event EventHandler<string> ItemChanged;

        private LiberatedStatus _bookStatus;
        private LiberatedStatus? _pdfStatus;
        public LiberatedStatus BookStatus
        {
            get => _bookStatus;
            set
            {
                if (_bookStatus != value)
				{
                    _bookStatus = value;
                    ItemChanged?.Invoke(this, nameof(BookStatus));
                }
            }
        }
        public LiberatedStatus? PdfStatus
        {
            get => _pdfStatus;
            set
            {
                if (_pdfStatus != value)
                {
                    _pdfStatus = value;
                    ItemChanged?.Invoke(this, nameof(PdfStatus));
                }
            }
        }
		#endregion

		#region batch changes
		public static event EventHandler<string> Batch_ItemChanged;
        public void BatchMode_UpdateBookStatus(LiberatedStatus value)
        {
            if (_bookStatus != value)
            {
                _bookStatus = value;
                batchFlag = true;
            }
        }

        // don't overwrite current with null. Therefore input is "LiberatedStatus" not "LiberatedStatus?"
        public void BatchMode_UpdatePdfStatus(LiberatedStatus value)
        {
            if (_pdfStatus != value)
            {
                _pdfStatus = value;
                batchFlag = true;
            }
        }

        private static bool batchFlag = false;

        public static void BatchMode_Finalize()
        {
            if (batchFlag)
                Batch_ItemChanged?.Invoke(null, null);

            batchFlag = false;
        }
        #endregion

        public override string ToString() => $"{Book} {Rating} {Tags}";
	}
}
