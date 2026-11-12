using System.IO;
using System.IO.Compression;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Extras.Metadata.ComicInfo;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Extras.Metadata.ComicInfo
{
    // A volume built from chapters has no printed page count: ComicInfo carries the pages the archive holds.
    [TestFixture]
    public class ComicInfoWriterFixture : CoreTest<ComicInfoWriter>
    {
        private string _cbz;
        private string _written;

        [SetUp]
        public void Setup()
        {
            _cbz = Path.Combine(Path.GetTempPath(), "mangarr-comicinfo-" + Path.GetRandomFileName() + ".cbz");
            _written = null;

            using (var zip = ZipFile.Open(_cbz, ZipArchiveMode.Create))
            {
                foreach (var name in new[] { "0001.jpg", "0002.png", "0003.webp", "ComicInfo.xml", "notes.txt" })
                {
                    using var entry = zip.CreateEntry(name).Open();
                    entry.WriteByte(0xFF);
                }
            }

            Mocker.GetMock<IEditionService>().Setup(s => s.GetEdition(5)).Returns(new Edition { Id = 5, PageCount = 192 });
            Mocker.GetMock<IComicInfoEmbedder>().Setup(e => e.CanEmbed(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IComicInfoEmbedder>()
                .Setup(e => e.Embed(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((p, xml) => _written = xml)
                .Returns(new ComicInfoEmbedResult { Status = ComicInfoEmbedStatus.Unchanged });
        }

        [TearDown]
        public void TearDown()
        {
            File.Delete(_cbz);
        }

        private void Write(Quality quality, string subtitle = null)
        {
            var file = new BookFile { Path = _cbz, EditionId = 5, Quality = new QualityModel(quality) };

            Subject.WriteForFile(new Author { Metadata = new AuthorMetadata { Name = "Sample Series" } }, new Book { VolumeNumber = 9, Title = "Sample Series Vol. 9", Subtitle = subtitle }, file);
        }

        // Volume titles (2026-11-07): the Title element reads like the volume page.
        [Test]
        public void a_volume_with_its_own_title_writes_it_into_the_title()
        {
            Write(Quality.CBZ, "The Long Night");

            _written.Should().Contain("<Title>Sample Series: The Long Night (Vol. 9)</Title>");
        }

        [Test]
        public void a_volume_without_a_title_keeps_the_book_title()
        {
            Write(Quality.CBZ);

            _written.Should().Contain("<Title>Sample Series Vol. 9</Title>");
        }

        [Test]
        public void a_junk_title_is_not_written()
        {
            Write(Quality.CBZ, "Volume Nine");

            _written.Should().Contain("<Title>Sample Series Vol. 9</Title>");
        }

        [Test]
        public void a_chapter_built_volume_gets_the_image_count_of_its_archive()
        {
            Write(Quality.CbzChapters);

            _written.Should().Contain("<PageCount>3</PageCount>");
        }

        [Test]
        public void a_fan_translation_gets_the_image_count_of_its_archive()
        {
            Write(Quality.CbzFanTranslation);

            _written.Should().Contain("<PageCount>3</PageCount>");
        }

        [Test]
        public void any_other_file_keeps_the_editions_page_count()
        {
            Write(Quality.CBZ);

            _written.Should().Contain("<PageCount>192</PageCount>");
        }
    }
}
