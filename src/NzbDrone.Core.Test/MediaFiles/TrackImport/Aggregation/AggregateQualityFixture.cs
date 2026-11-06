using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation.Aggregators;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Aggregation.Aggregators
{
    [TestFixture]
    public class AggregateQualityFixture : CoreTest<AggregateQuality>
    {
        // Volumes built from chapter packs (2026-10-21): the built file is graded by its own name, whatever
        // the chapter release's title says, and whether or not the generic file-name parse reads it.
        [Test]
        public void a_chapter_built_volume_grades_cbz_chapters_over_the_release_title()
        {
            var localBook = new LocalBook
            {
                Path = @"C:\Temp\mangarr-chapter-volumes\abc123\Sample Series Vol. 09 (Built from chapters 72–81).cbz".AsOsAgnostic(),
                FileTrackInfo = new ParsedTrackInfo(),
                DownloadClientBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.CBZ) }
            };

            Subject.Aggregate(localBook, false).Quality.Quality.Should().Be(Quality.CbzChapters);
        }

        // Fan translations (2026-11-05): graded by the file's own name, like a built volume.
        [Test]
        public void a_fan_translation_file_grades_cbz_fan_translation()
        {
            var localBook = new LocalBook
            {
                Path = @"C:\Downloads\Sample Series v05 (Fan translation, chapters 40-48).cbz".AsOsAgnostic(),
                FileTrackInfo = new ParsedTrackInfo { Quality = new QualityModel(Quality.CBZ) }
            };

            Subject.Aggregate(localBook, false).Quality.Quality.Should().Be(Quality.CbzFanTranslation);
        }

        // A marked release whose file lost the marker is still a fan copy: the download's title or its folder
        // carries the grade, so the file never imports as an official CBZ.
        [Test]
        public void an_archive_file_from_a_fan_translation_download_grades_cbz_fan_translation()
        {
            var fromDownload = new LocalBook
            {
                Path = @"C:\Downloads\Sample Series v05.cbz".AsOsAgnostic(),
                FileTrackInfo = new ParsedTrackInfo { Quality = new QualityModel(Quality.CBZ) },
                DownloadClientBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.CbzFanTranslation) }
            };

            var fromFolder = new LocalBook
            {
                Path = @"C:\Downloads\Sample Series v05.cbz".AsOsAgnostic(),
                FileTrackInfo = new ParsedTrackInfo { Quality = new QualityModel(Quality.CBZ) },
                FolderTrackInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.CbzFanTranslation) }
            };

            Subject.Aggregate(fromDownload, false).Quality.Quality.Should().Be(Quality.CbzFanTranslation);
            Subject.Aggregate(fromFolder, false).Quality.Quality.Should().Be(Quality.CbzFanTranslation);
        }

        // Only an archive file takes the fan grade: an ebook or an audiobook keeps its own, whether the marker is
        // in its name or in its download's title.
        [TestCase(@"C:\Downloads\Sample Series v05 (Fan translation, chapters 40-48).epub", "EPUB")]
        [TestCase(@"C:\Downloads\Sample Series v05 (Fan translation, chapters 40-48).m4b", "M4B")]
        public void an_ebook_or_audio_file_never_grades_cbz_fan_translation(string path, string fileQuality)
        {
            var quality = Quality.All.Find(q => q.Name == fileQuality);

            var markedName = new LocalBook
            {
                Path = path.AsOsAgnostic(),
                FileTrackInfo = new ParsedTrackInfo { Quality = new QualityModel(quality) }
            };

            var markedDownload = new LocalBook
            {
                Path = path.AsOsAgnostic(),
                FileTrackInfo = new ParsedTrackInfo { Quality = new QualityModel(quality) },
                DownloadClientBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.CbzFanTranslation) },
                FolderTrackInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.CbzFanTranslation) }
            };

            Subject.Aggregate(markedName, false).Quality.Quality.Should().Be(quality);
            Subject.Aggregate(markedDownload, false).Quality.Quality.Should().Be(quality);
        }

        [Test]
        public void a_plain_volume_keeps_the_quality_it_parsed()
        {
            var localBook = new LocalBook
            {
                Path = @"C:\Downloads\Sample Series v09 (Digital).cbz".AsOsAgnostic(),
                FileTrackInfo = new ParsedTrackInfo(),
                DownloadClientBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.CBZ) }
            };

            Subject.Aggregate(localBook, false).Quality.Quality.Should().Be(Quality.CBZ);
        }
    }
}
