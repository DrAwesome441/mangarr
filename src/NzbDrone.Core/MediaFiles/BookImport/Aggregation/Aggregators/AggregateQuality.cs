using System.IO;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.BookImport.Aggregation.Aggregators
{
    public class AggregateQuality : IAggregate<LocalBook>
    {
        public LocalBook Aggregate(LocalBook localTrack, bool otherFiles)
        {
            var quality = localTrack.FileTrackInfo?.Quality;

            if (quality == null)
            {
                quality = localTrack.FolderTrackInfo?.Quality;
            }

            if (quality == null)
            {
                quality = localTrack.DownloadClientBookInfo?.Quality;
            }

            // Volumes built from chapter packs (2026-10-21): the built file says what it is in its own name; the
            // generic name parse may not read it and the chapter release's title grades something else.
            if (localTrack.Path != null && QualityParser.IsBuiltFromChapters(Path.GetFileName(localTrack.Path)))
            {
                quality = new QualityModel(Quality.CbzChapters) { QualityDetectionSource = QualityDetectionSource.Name };
            }

            // Fan translations (2026-11-05): the same, by the "(Fan translation" marker in the file name, or in the
            // download's title or folder (a marked release whose file lost the marker is still a fan copy). Archive
            // files only: an ebook or audio file is never graded CBZ (fan translation).
            var fanTranslation = (localTrack.Path != null && QualityParser.IsFanTranslation(Path.GetFileName(localTrack.Path))) ||
                                 localTrack.DownloadClientBookInfo?.Quality?.Quality == Quality.CbzFanTranslation ||
                                 localTrack.FolderTrackInfo?.Quality?.Quality == Quality.CbzFanTranslation;

            if (fanTranslation && MediaTypes.OfExtension(Path.GetExtension(localTrack.Path)) == MediaType.Archive)
            {
                quality = new QualityModel(Quality.CbzFanTranslation) { QualityDetectionSource = QualityDetectionSource.Name };
            }

            localTrack.Quality = quality;
            return localTrack;
        }
    }
}
