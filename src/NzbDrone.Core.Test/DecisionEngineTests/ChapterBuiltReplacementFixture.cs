using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.CustomFormats;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    // Chapter-built volumes (2026-10): a real volume always replaces a CbzChapters stand-in, even
    // on a profile that forbids upgrades and whose cutoff the stand-in already meets. The real
    // UpgradeDiskSpecification is resolved here because UpgradeDiskSpecificationFixture is [Ignore]d.
    public static class ChapterBuiltFixtureData
    {
        public static RemoteBook MangaVolume(Quality onDisk, Quality incoming, bool chaptersRankedAboveCbz = false)
        {
            var profile = new QualityProfile
            {
                UpgradeAllowed = false,
                Cutoff = Quality.CBZ.Id,
                MinFormatScore = 0,
                FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems("None"),
                Items = new List<QualityProfileQualityItem>
                {
                    new QualityProfileQualityItem { Quality = Quality.CbzFanTranslation, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.CbzChapters, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.PDF, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.CBR, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.CBZ, Allowed = true }
                }
            };

            if (chaptersRankedAboveCbz)
            {
                // A profile whose order alone would call CBZ over CbzChapters a downgrade.
                profile.Items.Reverse();
            }

            var author = Builder<Author>.CreateNew().With(a => a.QualityProfile = profile).Build();

            var volume = new Book { Id = 7, Title = "Your Lie in April Vol. 7", VolumeNumber = 7 };
            volume.WithEdition(MediaType.Archive, new List<BookFile> { new BookFile { Id = 3, EditionId = 71, Quality = new QualityModel(onDisk), DateAdded = DateTime.UtcNow } });

            return new RemoteBook
            {
                Author = author,
                Books = new List<Book> { volume },
                MediaType = MediaType.Archive,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(incoming) },
                CustomFormats = new List<CustomFormat>()
            };
        }
    }

    [TestFixture]
    public class ReplacesStandInFixture
    {
        [Test]
        public void chapter_built_is_replaced_by_a_real_volume()
        {
            UpgradableSpecification.ReplacesStandIn(new QualityModel(Quality.CbzChapters), new QualityModel(Quality.CBZ)).Should().BeTrue();
        }

        [Test]
        public void chapter_built_is_not_replaced_by_chapter_built()
        {
            UpgradableSpecification.ReplacesStandIn(new QualityModel(Quality.CbzChapters), new QualityModel(Quality.CbzChapters)).Should().BeFalse();
        }

        [Test]
        public void chapter_built_is_not_replaced_by_an_unknown_quality()
        {
            UpgradableSpecification.ReplacesStandIn(new QualityModel(Quality.CbzChapters), new QualityModel(Quality.Unknown)).Should().BeFalse();
        }

        [Test]
        public void a_real_volume_is_not_a_chapter_built_replacement()
        {
            UpgradableSpecification.ReplacesStandIn(new QualityModel(Quality.CBZ), new QualityModel(Quality.CBZ)).Should().BeFalse();
        }

        // Fan translations (2026-11-05): any official copy replaces a fan copy, a fan copy replaces nothing.
        [TestCase(14, 2, true)]   // fan -> CBZ
        [TestCase(14, 9, true)]   // fan -> CbzChapters
        [TestCase(14, 1, true)]   // fan -> PDF
        [TestCase(14, 14, false)] // fan -> fan
        [TestCase(14, 0, false)]  // fan -> Unknown
        [TestCase(9, 14, false)]  // chapters -> fan
        [TestCase(2, 14, false)]  // CBZ -> fan
        public void fan_translation_stand_in_rules(int existingId, int incomingId, bool expected)
        {
            UpgradableSpecification.ReplacesStandIn(new QualityModel(Quality.All.Single(q => q.Id == existingId)), new QualityModel(Quality.All.Single(q => q.Id == incomingId))).Should().Be(expected);
        }

        [Test]
        public void a_missing_quality_is_never_replaced_or_replacing()
        {
            UpgradableSpecification.ReplacesStandIn(null, new QualityModel(Quality.CBZ)).Should().BeFalse();
            UpgradableSpecification.ReplacesStandIn(new QualityModel(Quality.CbzFanTranslation), null).Should().BeFalse();
        }

        [Test]
        public void only_chapters_and_fan_translations_are_stand_ins()
        {
            UpgradableSpecification.IsStandIn(new QualityModel(Quality.CbzChapters)).Should().BeTrue();
            UpgradableSpecification.IsStandIn(new QualityModel(Quality.CbzFanTranslation)).Should().BeTrue();
            UpgradableSpecification.IsStandIn(new QualityModel(Quality.CBZ)).Should().BeFalse();
            UpgradableSpecification.IsStandIn(null).Should().BeFalse();
        }
    }

    [TestFixture]
    public class ChapterBuiltUpgradeDiskFixture : CoreTest<UpgradeDiskSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(x => x.ParseCustomFormat(It.IsAny<BookFile>())).Returns(new List<CustomFormat>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void an_official_volume_replaces_a_fan_translation(bool fanRankedAboveCbz)
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzFanTranslation, Quality.CBZ, chaptersRankedAboveCbz: fanRankedAboveCbz), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_chapter_built_volume_replaces_a_fan_translation()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzFanTranslation, Quality.CbzChapters), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void real_volume_replaces_the_chapter_built_file()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CBZ), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void real_volume_replaces_the_chapter_built_file_on_a_profile_that_ranks_chapters_above_cbz()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CBZ, chaptersRankedAboveCbz: true), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void chapter_built_over_chapter_built_is_rejected()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CbzChapters), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void cbz_over_cbz_is_rejected_as_before()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CBZ, Quality.CBZ), null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class ChapterBuiltUpgradeAllowedFixture : CoreTest<UpgradeAllowedSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(x => x.ParseCustomFormat(It.IsAny<BookFile>(), It.IsAny<Author>())).Returns(new List<CustomFormat>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void an_official_volume_replaces_a_fan_translation(bool fanRankedAboveCbz)
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzFanTranslation, Quality.CBZ, chaptersRankedAboveCbz: fanRankedAboveCbz), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_chapter_built_volume_replaces_a_fan_translation()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzFanTranslation, Quality.CbzChapters), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void real_volume_replaces_the_chapter_built_file_although_upgrades_are_off()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CBZ), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void cbz_over_cbz_stays_accepted_here_as_before()
        {
            // Equal quality is no upgrade, so this spec has nothing to forbid (UpgradeDisk rejects it).
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CBZ, Quality.CBZ), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_real_upgrade_of_a_real_volume_is_still_forbidden()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.PDF, Quality.CBZ), null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class ChapterBuiltCutoffFixture : CoreTest<CutoffSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(x => x.ParseCustomFormat(It.IsAny<BookFile>())).Returns(new List<CustomFormat>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void an_official_volume_replaces_a_fan_translation(bool fanRankedAboveCbz)
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzFanTranslation, Quality.CBZ, chaptersRankedAboveCbz: fanRankedAboveCbz), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_chapter_built_volume_replaces_a_fan_translation()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzFanTranslation, Quality.CbzChapters), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void real_volume_replaces_the_chapter_built_file_although_it_is_the_cutoff()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CBZ), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void chapter_built_over_chapter_built_is_rejected()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CbzChapters), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void cbz_over_cbz_is_rejected_as_before()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CBZ, Quality.CBZ), null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class FanTranslationCutoffFixture : CoreTest<UpgradableSpecification>
    {
        [Test]
        public void a_fan_translation_never_meets_the_cutoff()
        {
            var profile = new QualityProfile
            {
                UpgradeAllowed = false,
                Cutoff = Quality.CbzFanTranslation.Id,
                Items = new List<QualityProfileQualityItem>
                {
                    new QualityProfileQualityItem { Quality = Quality.CbzFanTranslation, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.CBZ, Allowed = false }
                }
            };

            Subject.QualityCutoffNotMet(profile, new QualityModel(Quality.CbzFanTranslation)).Should().BeTrue();
        }
    }
}
