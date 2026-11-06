using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Books.Chapters
{
    // One volume a chapter release completes: the chapters that go into it, in reading order.
    public class PlannedVolume
    {
        public Book Book { get; set; }
        public List<double> Chapters { get; set; }
        public double First { get; set; }
        public double Last { get; set; }
    }

    // Volumes built from chapter packs (2026-10-21): which volumes one release completes on its own.
    // A volume counts only when its own edition lists its chapters and every whole one is available;
    // a half chapter (n.5) is never required: when available it goes where an edition lists it, else
    // after chapter n. Nothing is borrowed from another edition and nothing is inferred; volumes whose
    // lists share a whole chapter (a catalogue error) are never planned; nor is a volume whose list does
    // not adjoin a listed neighbour's (2026-11-05: a first-chapter-only list is not the whole volume).
    public static class ChapterVolumePlanner
    {
        public static List<PlannedVolume> Plan(IReadOnlyCollection<double> available, IEnumerable<Book> books, MediaType mediaType)
        {
            var have = new HashSet<double>(available.Select(Key));
            var bookList = books.ToList();

            // A half chapter an edition lists goes where it is listed; only one no edition lists falls
            // back to "after chapter n".
            var listedHalves = new HashSet<double>(bookList
                .SelectMany(b => b.EditionOf(mediaType)?.Chapters ?? new List<double>())
                .Where(c => !IsWhole(c))
                .Select(Key));
            var unlistedHalves = available.Where(c => !IsWhole(c) && !listedHalves.Contains(Key(c))).ToList();
            var untrusted = Overlapping(bookList, mediaType);
            untrusted.UnionWith(Unadjoined(bookList, mediaType));
            var result = new List<PlannedVolume>();

            foreach (var book in bookList)
            {
                var listed = book.EditionOf(mediaType)?.Chapters;

                if (listed == null || listed.Count == 0 || untrusted.Contains(book.Id))
                {
                    continue;
                }

                var whole = listed.Where(IsWhole).Distinct().ToList();

                if (whole.Count == 0 || !whole.All(c => have.Contains(Key(c))))
                {
                    continue;
                }

                var wholeSet = new HashSet<double>(whole);
                var chapters = whole
                    .Concat(listed.Where(c => !IsWhole(c) && have.Contains(Key(c))))
                    .Concat(unlistedHalves.Where(h => wholeSet.Contains(Math.Floor(h))))
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                result.Add(new PlannedVolume
                {
                    Book = book,
                    Chapters = chapters,
                    First = chapters.First(),
                    Last = chapters.Last()
                });
            }

            return result;
        }

        // The chapters a release's range makes available: every whole chapter in [start, end], plus
        // start / end themselves when fractional. "c001-073" says nothing about 73.5.
        public static List<double> Available(double start, double end)
        {
            var result = new List<double>();

            for (var c = Math.Ceiling(start); c <= end; c++)
            {
                result.Add(c);
            }

            if (!IsWhole(start))
            {
                result.Add(start);
            }

            if (!IsWhole(end) && end != start)
            {
                result.Add(end);
            }

            return result;
        }

        // Volumes (book ids) whose edition lists a whole chapter another volume's edition also lists: a
        // catalogue error, and which volume the chapter belongs to would be a guess. A shared half chapter
        // is not one (it is placed where listed, never required).
        public static HashSet<int> Overlapping(IEnumerable<Book> books, MediaType mediaType)
        {
            return books
                .SelectMany(b => (b.EditionOf(mediaType)?.Chapters ?? new List<double>())
                    .Where(IsWhole)
                    .Select(Key)
                    .Distinct()
                    .Select(c => (Book: b.Id, Chapter: c)))
                .GroupBy(x => x.Chapter)
                .Where(g => g.Select(x => x.Book).Distinct().Count() > 1)
                .SelectMany(g => g.Select(x => x.Book))
                .ToHashSet();
        }

        // Volumes (book ids) whose list is not backed by a neighbour (2026-11-05): about 17% of catalogue
        // lists hold only the volume's first chapter, and such a list says nothing of the rest. A list
        // is trusted only when the volume next to it (N-1 / N+1, same series, listing a whole chapter)
        // starts or ends right beside it and no neighbour contradicts it. No neighbour = not trusted.
        // A list of one whole chapter always has the right start, so only the NEXT volume can confirm
        // it. Whole chapters must run unbroken; a list with a hole is refused and its end confirms no
        // one (its start still can). A volume number held by two listed books is ambiguous: neither is
        // trusted and neither is anyone's neighbour. A volume number that is zero, negative or fractional
        // cannot be confirmed, so such a volume is refused and is nobody's neighbour; neither is a
        // volume whose list overlaps another's. Nothing is inferred from a neighbour.
        public static HashSet<int> Unadjoined(IEnumerable<Book> books, MediaType mediaType)
        {
            var bookList = books.ToList();
            var overlapping = Overlapping(bookList, mediaType);
            var listed = bookList
                .Select(b => (Book: b, Whole: (b.EditionOf(mediaType)?.Chapters ?? new List<double>()).Where(IsWhole).Distinct().OrderBy(c => c).ToList()))
                .Where(x => x.Whole.Count > 0)
                .ToList();
            var result = new HashSet<int>();

            // A zero, negative or fractional volume number cannot be confirmed by a neighbour (2026-11-05).
            result.UnionWith(listed.Where(x => x.Book.VolumeNumber <= 0 || !IsWhole(x.Book.VolumeNumber)).Select(x => x.Book.Id));

            // A volume whose list overlaps another's confirms no one.
            var entries = listed
                .Where(x => x.Book.VolumeNumber > 0 && IsWhole(x.Book.VolumeNumber) && !overlapping.Contains(x.Book.Id))
                .ToList();
            var spans = new Dictionary<double, (double First, double Last, bool Unbroken, int BookId)>();

            foreach (var group in entries.GroupBy(x => x.Book.VolumeNumber))
            {
                if (group.Select(x => x.Book.Id).Distinct().Count() > 1)
                {
                    result.UnionWith(group.Select(x => x.Book.Id));
                    continue;
                }

                var whole = group.First().Whole;
                spans[group.Key] = (whole.First(), whole.Last(), whole.Zip(whole.Skip(1), (x, y) => y - x).All(d => d == 1), group.First().Book.Id);
            }

            foreach (var span in spans)
            {
                var number = span.Key;
                var mine = span.Value;

                if (!mine.Unbroken)
                {
                    result.Add(mine.BookId);
                    continue;
                }

                var hasPrevious = spans.TryGetValue(number - 1, out var previous);
                var hasNext = spans.TryGetValue(number + 1, out var next);
                var previousAdjoins = hasPrevious && previous.Unbroken && previous.Last + 1 == mine.First;
                var nextAdjoins = hasNext && mine.Last + 1 == next.First;
                var single = mine.First == mine.Last;

                if ((!hasPrevious && !hasNext) || (hasPrevious && !previousAdjoins) || (hasNext && !nextAdjoins) || (single && !nextAdjoins))
                {
                    result.Add(mine.BookId);
                }
            }

            return result;
        }

        // Chapter data (2026-11-06): what the interface may show -- the chapters of every volume whose
        // list the builder itself would use: no whole chapter shared with another volume, and adjoining a
        // listed neighbour (Overlapping, Unadjoined). First / Last are the lowest and highest chapter the
        // list holds, a listed half chapter included. books = every volume of ONE series: a neighbour
        // that is not passed in can neither confirm nor contradict.
        public static Dictionary<int, (double First, double Last)> TrustedSpans(IEnumerable<Book> books, MediaType mediaType)
        {
            var bookList = books.ToList();
            var untrusted = Overlapping(bookList, mediaType);
            untrusted.UnionWith(Unadjoined(bookList, mediaType));
            var result = new Dictionary<int, (double First, double Last)>();

            foreach (var book in bookList)
            {
                var listed = book.EditionOf(mediaType)?.Chapters;

                if (listed == null || !listed.Any(IsWhole) || untrusted.Contains(book.Id))
                {
                    continue;
                }

                result[book.Id] = (listed.Min(), listed.Max());
            }

            return result;
        }

        public static bool IsEnabled(Author author, IConfigService config)
        {
            return author != null &&
                   author.Library == LibraryType.Manga &&
                   config.BuildVolumesFromChapters &&
                   author.BuildFromChapters;
        }

        private static bool IsWhole(double chapter)
        {
            return chapter == Math.Floor(chapter);
        }

        private static double Key(double chapter)
        {
            return Math.Round(chapter, 2);
        }
    }
}
