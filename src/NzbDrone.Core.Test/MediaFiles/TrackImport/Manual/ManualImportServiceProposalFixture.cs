using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Manual
{
    // Manual import of a tracked grab (2026-11-06): a one-volume manga grab whose one file does not say
    // which volume it is is proposed as the grabbed volume, by the automatic import's own rule. Before,
    // it was scored against the library and offered as a volume already owned, with no rejection.
    [TestFixture]
    public class ManualImportServiceProposalFixture : CoreTest<ManualImportService>
    {
        private const string DownloadId = "SABnzbd_nzo_sample";

        private readonly string _folder = @"C:\drop\Publisher-Sample.Saga.Vol.13.Some.Subtitle.2020.Hybrid.Comic.eBook-GRP".AsOsAgnostic();
        private Author _author;
        private Book _volume;
        private Edition _archive;
        private IdentificationOverrides _seen;

        [SetUp]
        public void Setup()
        {
            _author = new Author { Id = 5, Name = "Sample Saga", Metadata = new AuthorMetadata { Name = "Sample Saga", ForeignAuthorId = "local-sample-saga" } };
            _volume = new Book { Id = 9, VolumeNumber = 13 };
            _archive = _volume.WithEdition(MediaType.Archive, id: 91);
            _seen = null;

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(_folder)).Returns(true);

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((files, overrides, info, config) => _seen = overrides)
                .Returns<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((files, overrides, info, config) =>
                    files.Select(f => new ImportDecision<LocalBook>(new LocalBook
                    {
                        Path = f.FullName,
                        Author = overrides.Author,
                        Book = overrides.Book,
                        Edition = overrides.Edition,
                        FileTrackInfo = new ParsedTrackInfo()
                    })).ToList());
        }

        private void GivenGrab(params Book[] books)
        {
            Mocker.GetMock<ITrackedDownloadService>()
                .Setup(s => s.Find(DownloadId))
                .Returns(new TrackedDownload
                {
                    DownloadItem = new DownloadClientItem { DownloadId = DownloadId, Title = "Publisher-Sample.Saga.Vol.13.Some.Subtitle.2020.Hybrid.Comic.eBook-GRP" },
                    ImportItem = new DownloadClientItem { OutputPath = new OsPath(_folder) },
                    RemoteBook = new RemoteBook { Author = _author, Books = books.ToList() }
                });
        }

        private void GivenFiles(params string[] names)
        {
            Mocker.GetMock<IDiskScanService>()
                .Setup(s => s.GetBookFiles(_folder, It.IsAny<bool>()))
                .Returns(names.Select(n =>
                {
                    var info = new Mock<IFileInfo>();
                    info.SetupGet(i => i.Name).Returns(n);
                    info.SetupGet(i => i.FullName).Returns(System.IO.Path.Combine(_folder, n));
                    info.SetupGet(i => i.Extension).Returns(System.IO.Path.GetExtension(n));
                    return info.Object;
                }).ToArray());
        }

        [TestCase("xx-sample.saga.volume..pdf")]
        [TestCase("xx-sample.saga.13.some..pdf")]
        public void a_one_volume_manga_grab_whose_file_names_no_volume_is_proposed_as_the_grabbed_volume(string fileName)
        {
            GivenGrab(_volume);
            GivenFiles(fileName);

            var items = Subject.GetMediaFiles(null, DownloadId, null, FilterFilesType.None, false);

            _seen.Book.Should().BeSameAs(_volume);
            _seen.Edition.Should().BeSameAs(_archive);
            items.Should().ContainSingle().Which.Book.Should().BeSameAs(_volume);
        }

        [TestCase("Sample Saga v12.cbz")]
        [TestCase("Sample Saga 12.cbz")]
        [TestCase("Sample Saga Chapter 13.cbz")]
        public void a_file_that_names_another_volume_or_a_chapter_keeps_the_usual_proposal(string fileName)
        {
            GivenGrab(_volume);
            GivenFiles(fileName);

            Subject.GetMediaFiles(null, DownloadId, null, FilterFilesType.None, false);

            _seen.Author.Should().BeSameAs(_author);
            _seen.Book.Should().BeNull();
            _seen.Edition.Should().BeNull();
        }

        [Test]
        public void a_grab_of_several_volumes_or_a_folder_of_several_files_keeps_the_usual_proposal()
        {
            GivenGrab(_volume, new Book { Id = 10, VolumeNumber = 14 });
            GivenFiles("xx-sample.saga.volume..pdf");

            Subject.GetMediaFiles(null, DownloadId, null, FilterFilesType.None, false);

            _seen.Book.Should().BeNull();

            GivenGrab(_volume);
            GivenFiles("xx-sample.saga.volume..pdf", "xxabcde.part1.rar");

            Subject.GetMediaFiles(null, DownloadId, null, FilterFilesType.None, false);

            _seen.Book.Should().BeNull();
        }
    }
}
