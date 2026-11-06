using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    // Fan translations (2026-11-05): a fan copy is only taken for a released volume, 14 days after its
    // release, and never replaces a file.
    [TestFixture]
    public class FanTranslationSpecificationFixture : CoreTest<FanTranslationSpecification>
    {
        private static RemoteBook GivenRelease(Quality quality, DateTime? releaseDate, string precision = null, params Quality[] onDisk)
        {
            var book = new Book { Id = 7, Title = "Sample Series Vol. 7", VolumeNumber = 7, ReleaseDate = releaseDate, ReleaseDatePrecision = precision };
            book.WithEdition(MediaType.Archive, onDisk.Select(q => new BookFile { Id = 3, EditionId = 71, Quality = new QualityModel(q) }).ToList());

            return new RemoteBook
            {
                Books = new List<Book> { book },
                MediaType = MediaType.Archive,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(quality) }
            };
        }

        [Test]
        public void accepts_a_release_that_is_not_a_fan_translation()
        {
            Subject.IsSatisfiedBy(GivenRelease(Quality.CBZ, null), null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(GivenRelease(Quality.CBZ, DateTime.UtcNow.AddDays(3), null, Quality.CBZ), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void accepts_a_fan_translation_released_15_days_ago_with_no_file()
        {
            Subject.IsSatisfiedBy(GivenRelease(Quality.CbzFanTranslation, DateTime.UtcNow.AddDays(-15)), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void rejects_a_fan_translation_released_13_days_ago()
        {
            var decision = Subject.IsSatisfiedBy(GivenRelease(Quality.CbzFanTranslation, DateTime.UtcNow.AddDays(-13)), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("14 days");
        }

        [Test]
        public void rejects_a_fan_translation_for_a_future_volume()
        {
            Subject.IsSatisfiedBy(GivenRelease(Quality.CbzFanTranslation, DateTime.UtcNow.AddDays(20)), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void rejects_a_fan_translation_for_an_undated_volume()
        {
            var decision = Subject.IsSatisfiedBy(GivenRelease(Quality.CbzFanTranslation, null), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("no release date");
        }

        // Coarse dates are stored at the end of their period (EditionDates.Resolve), so the wait runs from
        // the stored date whatever the precision.
        [TestCase("month")]
        [TestCase("year")]
        public void a_coarse_date_waits_14_days_from_the_stored_date(string precision)
        {
            Subject.IsSatisfiedBy(GivenRelease(Quality.CbzFanTranslation, DateTime.UtcNow.AddDays(-15), precision), null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(GivenRelease(Quality.CbzFanTranslation, DateTime.UtcNow.AddDays(-13), precision), null).Accepted.Should().BeFalse();
        }

        [TestCase(2)]  // CBZ
        [TestCase(9)]  // CbzChapters
        [TestCase(14)] // CbzFanTranslation
        [TestCase(0)]  // Unknown
        public void rejects_a_fan_translation_when_the_volume_has_a_file(int onDiskId)
        {
            var onDisk = Quality.All.Single(q => q.Id == onDiskId);

            var decision = Subject.IsSatisfiedBy(GivenRelease(Quality.CbzFanTranslation, DateTime.UtcNow.AddDays(-60), null, onDisk), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("never replaces");
        }
    }
}
