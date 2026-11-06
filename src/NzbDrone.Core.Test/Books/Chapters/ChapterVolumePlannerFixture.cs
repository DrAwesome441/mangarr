using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Chapters;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests.Chapters
{
    // Volumes built from chapter packs (2026-10-21): a volume is planned only when every whole
    // chapter its own edition lists is available; half chapters ride along, never required.
    [TestFixture]
    public class ChapterVolumePlannerFixture : CoreTest
    {
        private static readonly (int Volume, int First, int Last)[] Layout =
        {
            (1, 1, 6), (2, 7, 15), (3, 16, 24), (4, 25, 33), (5, 34, 42),
            (6, 43, 52), (7, 53, 61), (8, 62, 71), (9, 72, 81)
        };

        private static Book Volume(int number, List<double> chapters, MediaType mediaType = MediaType.Archive)
        {
            return new Book
            {
                Id = number,
                VolumeNumber = number,
                Editions = new List<Edition> { new Edition { MediaType = mediaType, Chapters = chapters } }
            };
        }

        private static List<double> Range(int first, int last)
        {
            return Enumerable.Range(first, last - first + 1).Select(n => (double)n).ToList();
        }

        private static List<Book> Series()
        {
            return Layout.Select(v => Volume(v.Volume, Range(v.First, v.Last))).ToList();
        }

        [Test]
        public void chapters_1_to_73_plan_the_first_eight_volumes_only()
        {
            var plan = ChapterVolumePlanner.Plan(Range(1, 73), Series(), MediaType.Archive);

            plan.Select(p => p.Book.VolumeNumber).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
            plan[0].First.Should().Be(1);
            plan[0].Last.Should().Be(6);
            plan[7].Chapters.Should().Equal(Range(62, 71));
        }

        [Test]
        public void a_missing_whole_chapter_plans_nothing()
        {
            var books = new List<Book> { Volume(1, Range(1, 6)), Volume(2, Range(7, 12)) };
            var plan = ChapterVolumePlanner.Plan(new List<double> { 1, 2, 3, 4, 6 }, books, MediaType.Archive);

            plan.Should().BeEmpty();
        }

        [Test]
        public void an_available_half_chapter_is_placed_after_its_chapter()
        {
            var available = Range(72, 81);
            available.Add(73.5);

            var plan = ChapterVolumePlanner.Plan(available, Series(), MediaType.Archive);

            plan.Should().ContainSingle();
            plan[0].Book.VolumeNumber.Should().Be(9);
            plan[0].Chapters.Should().Equal(72, 73, 73.5, 74, 75, 76, 77, 78, 79, 80, 81);
            plan[0].First.Should().Be(72);
            plan[0].Last.Should().Be(81);
        }

        [Test]
        public void a_listed_half_chapter_is_never_required()
        {
            var chapters = Range(1, 6);
            chapters.Insert(3, 3.5);

            // Vol. 2 adjoins (2026-11-05): a lone listed volume is no longer trusted.
            var books = new List<Book> { Volume(1, chapters), Volume(2, Range(7, 12)) };

            var plan = ChapterVolumePlanner.Plan(Range(1, 6), books, MediaType.Archive);

            plan.Should().ContainSingle();
            plan[0].Chapters.Should().Equal(Range(1, 6));
        }

        [Test]
        public void a_half_chapter_goes_to_the_volume_that_lists_it()
        {
            var second = Range(7, 12);
            second.Insert(0, 6.5);
            var books = new List<Book> { Volume(1, Range(1, 6)), Volume(2, second) };
            var available = Range(1, 12);
            available.Add(6.5);

            var plan = ChapterVolumePlanner.Plan(available, books, MediaType.Archive);

            plan[0].Chapters.Should().Equal(Range(1, 6));
            plan[1].Chapters.Should().Equal(6.5, 7, 8, 9, 10, 11, 12);
            plan[1].First.Should().Be(6.5);
        }

        // A catalogue error: Vol. 2 lists chapter 6, which Vol. 1 lists too. Neither is planned; the rest are.
        [Test]
        public void volumes_whose_whole_chapters_overlap_are_never_planned()
        {
            var series = Series();
            series[1] = Volume(2, Range(6, 15));

            ChapterVolumePlanner.Plan(Range(1, 73), series, MediaType.Archive)
                .Select(p => p.Book.VolumeNumber).Should().Equal(3, 4, 5, 6, 7, 8);
            ChapterVolumePlanner.Overlapping(series, MediaType.Archive).Should().BeEquivalentTo(new[] { 1, 2 });
        }

        [Test]
        public void a_shared_half_chapter_is_not_an_overlap()
        {
            var series = new List<Book> { Volume(1, new List<double> { 1, 2, 2.5 }), Volume(2, new List<double> { 2.5, 3, 4 }) };

            ChapterVolumePlanner.Overlapping(series, MediaType.Archive).Should().BeEmpty();
        }

        [Test]
        public void a_volume_with_no_chapter_list_is_never_planned()
        {
            var books = new List<Book> { Volume(1, null), Volume(2, new List<double>()) };

            ChapterVolumePlanner.Plan(Range(1, 100), books, MediaType.Archive).Should().BeEmpty();
        }

        [Test]
        public void only_the_edition_of_the_media_type_counts()
        {
            var books = new List<Book> { Volume(1, Range(1, 6), MediaType.Ebook) };

            ChapterVolumePlanner.Plan(Range(1, 6), books, MediaType.Archive).Should().BeEmpty();
        }

        [Test]
        public void the_edition_chapter_list_is_never_mutated()
        {
            var chapters = Range(72, 81);
            var available = Range(72, 81);
            available.Add(73.5);

            // Vol. 8 adjoins, so the planner reaches the code that could touch the list.
            var plan = ChapterVolumePlanner.Plan(available, new List<Book> { Volume(8, Range(62, 71)), Volume(9, chapters) }, MediaType.Archive);

            plan.Should().ContainSingle().Which.Chapters.Should().Contain(73.5);
            chapters.Should().Equal(Range(72, 81));
        }

        // Adjacency (2026-11-05): a list is trusted only when it adjoins a listed neighbour and
        // contradicts none; about 17% of catalogue lists hold just the volume's first chapter.
        private static List<int> Planned(List<Book> books)
        {
            return ChapterVolumePlanner.Plan(Range(1, 100), books, MediaType.Archive).Select(p => (int)p.Book.VolumeNumber).ToList();
        }

        [Test]
        public void first_chapter_only_lists_plan_nothing()
        {
            var books = new List<Book>
            {
                Volume(1, new List<double> { 1 }),
                Volume(2, new List<double> { 12 }),
                Volume(3, new List<double> { 22 })
            };

            Planned(books).Should().BeEmpty();
            ChapterVolumePlanner.Unadjoined(books, MediaType.Archive).Should().BeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Test]
        public void adjoining_lists_plan_every_volume()
        {
            var books = new List<Book> { Volume(1, Range(1, 7)), Volume(2, Range(8, 14)), Volume(3, Range(15, 21)) };

            Planned(books).Should().Equal(1, 2, 3);
            ChapterVolumePlanner.Unadjoined(books, MediaType.Archive).Should().BeEmpty();
        }

        [Test]
        public void a_lone_listed_volume_plans_nothing()
        {
            var books = new List<Book> { Volume(1, Range(1, 7)), Volume(2, null), Volume(3, new List<double>()) };

            Planned(books).Should().BeEmpty();
        }

        [Test]
        public void the_last_listed_volume_is_planned_after_an_adjoining_one()
        {
            var books = new List<Book> { Volume(1, Range(1, 7)), Volume(2, Range(8, 14)), Volume(3, null) };

            Planned(books).Should().Equal(1, 2);
        }

        [Test]
        public void one_adjoining_and_one_contradicting_neighbour_plans_only_the_first_volume()
        {
            var books = new List<Book> { Volume(1, Range(1, 7)), Volume(2, Range(8, 14)), Volume(3, new List<double> { 20 }) };

            Planned(books).Should().Equal(1);
            ChapterVolumePlanner.Unadjoined(books, MediaType.Archive).Should().BeEquivalentTo(new[] { 2, 3 });
        }

        [Test]
        public void a_half_chapter_still_rides_with_its_volume()
        {
            var books = new List<Book> { Volume(1, Range(1, 7)), Volume(2, Range(8, 14)) };

            var plan = ChapterVolumePlanner.Plan(Range(1, 14).Concat(new[] { 7.5 }).ToList(), books, MediaType.Archive);

            plan.Select(p => p.Book.VolumeNumber).Should().Equal(1, 2);
            plan[0].Chapters.Should().Equal(1, 2, 3, 4, 5, 6, 7, 7.5);
        }

        [Test]
        public void a_gap_in_volume_numbers_plans_neither_side()
        {
            var books = new List<Book> { Volume(1, Range(1, 7)), Volume(3, Range(15, 21)) };

            Planned(books).Should().BeEmpty();
        }

        // 2026-11-05: a neighbour cannot confirm a fractional volume number, so its list is not trusted.
        [Test]
        public void a_non_whole_volume_number_is_not_planned()
        {
            var book = Volume(1, Range(1, 7));
            book.VolumeNumber = 1.5;

            Planned(new List<Book> { book }).Should().BeEmpty();
            ChapterVolumePlanner.Unadjoined(new List<Book> { book }, MediaType.Archive).Should().BeEquivalentTo(new[] { 1 });
        }

        [Test]
        public void volume_zero_is_not_planned_from_a_first_chapter_only_list()
        {
            var book = Volume(1, new List<double> { 1 });
            book.VolumeNumber = 0;

            Planned(new List<Book> { book }).Should().BeEmpty();
        }

        [Test]
        public void an_overlapping_volume_confirms_no_neighbour()
        {
            var books = new List<Book> { Volume(1, Range(1, 7)), Volume(2, Range(7, 13)), Volume(3, Range(14, 20)) };

            Planned(books).Should().BeEmpty();
        }

        [Test]
        public void the_newest_volume_with_a_first_chapter_only_list_is_not_planned()
        {
            var books = new List<Book> { Volume(9, Range(72, 81)), Volume(10, new List<double> { 82 }), Volume(11, null) };

            Planned(books).Should().Equal(9);
        }

        [Test]
        public void a_first_chapter_only_list_confirmed_by_the_next_volume_is_planned()
        {
            var books = new List<Book> { Volume(1, new List<double> { 1 }), Volume(2, Range(2, 8)) };

            Planned(books).Should().Equal(1, 2);
        }

        // A hole in a list (chapter 10 missing) refuses that list and its end confirms no one; its start
        // still confirms the volume before it.
        [Test]
        public void a_list_with_a_hole_is_not_planned_and_does_not_confirm_its_next_volume()
        {
            var books = new List<Book> { Volume(1, Range(1, 7)), Volume(2, new List<double> { 8, 9, 11, 12 }), Volume(3, Range(13, 19)) };

            Planned(books).Should().Equal(1);
        }

        [Test]
        public void a_volume_number_held_by_two_listed_books_is_ambiguous()
        {
            var twin = Volume(2, Range(30, 36));
            twin.Id = 22;
            var books = new List<Book> { Volume(1, Range(1, 7)), Volume(2, Range(8, 14)), twin, Volume(3, Range(15, 21)) };

            // Neither Vol. 2 is trusted or confirms a neighbour: Vol. 1 and Vol. 3 have none left.
            Planned(books).Should().BeEmpty();
            ChapterVolumePlanner.Unadjoined(books, MediaType.Archive).Should().BeEquivalentTo(new[] { 1, 2, 22, 3 });
        }

        [Test]
        public void an_unnumbered_volume_is_refused_and_is_nobodys_neighbour()
        {
            var unnumbered = Volume(5, Range(8, 14));
            unnumbered.VolumeNumber = 0;
            var books = new List<Book> { Volume(1, Range(1, 7)), unnumbered };

            ChapterVolumePlanner.Unadjoined(books, MediaType.Archive).Should().BeEquivalentTo(new[] { 1, 5 });
        }

        // Chapter data (2026-11-06): the interface shows a volume's chapters only where the builder
        // would use the list.
        [Test]
        public void trusted_spans_are_the_first_and_last_chapter_of_every_adjoining_volume()
        {
            var spans = ChapterVolumePlanner.TrustedSpans(Series(), MediaType.Archive);

            spans.Keys.Should().BeEquivalentTo(Layout.Select(v => v.Volume));
            spans[1].Should().Be((1d, 6d));
            spans[9].Should().Be((72d, 81d));
        }

        [Test]
        public void trusted_spans_leave_out_first_chapter_only_lists()
        {
            var books = Layout.Select(v => Volume(v.Volume, new List<double> { v.First })).ToList();

            ChapterVolumePlanner.TrustedSpans(books, MediaType.Archive).Should().BeEmpty();
        }

        [Test]
        public void trusted_spans_leave_out_a_lone_listed_volume_and_overlapping_ones()
        {
            ChapterVolumePlanner.TrustedSpans(new List<Book> { Volume(1, Range(1, 6)), Volume(2, null) }, MediaType.Archive).Should().BeEmpty();

            var overlapping = new List<Book> { Volume(1, Range(1, 7)), Volume(2, Range(7, 12)), Volume(3, Range(13, 19)) };

            ChapterVolumePlanner.TrustedSpans(overlapping, MediaType.Archive).Should().BeEmpty();
        }

        [Test]
        public void trusted_spans_include_a_listed_half_chapter()
        {
            var first = Range(1, 6);
            first.Add(6.5);
            var books = new List<Book> { Volume(1, first), Volume(2, Range(7, 12)) };

            var spans = ChapterVolumePlanner.TrustedSpans(books, MediaType.Archive);

            spans[1].Should().Be((1d, 6.5d));
            spans[2].Should().Be((7d, 12d));
        }

        [Test]
        public void trusted_spans_read_only_the_edition_of_the_media_type_and_skip_a_list_of_halves()
        {
            var books = new List<Book>
            {
                Volume(1, Range(1, 6), MediaType.Ebook),
                Volume(2, Range(7, 12), MediaType.Ebook),
                Volume(3, new List<double> { 12.5 })
            };

            ChapterVolumePlanner.TrustedSpans(books, MediaType.Archive).Should().BeEmpty();
        }
    }
}
