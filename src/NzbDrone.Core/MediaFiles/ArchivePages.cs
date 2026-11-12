using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NzbDrone.Core.MediaFiles.Chapters;

namespace NzbDrone.Core.MediaFiles
{
    // The pages a CBZ holds: its image entries (the page extensions the chapter assembler writes),
    // zero-length and Mac resource entries left out. Null when the archive cannot be read, so a
    // caller never mistakes "unreadable" for "empty".
    public static class ArchivePages
    {
        public static int? Count(string path)
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);

                return zip.Entries.Count(e => e.Length > 0 &&
                                              ChapterVolumeAssembler.PageExtensions.Contains(Path.GetExtension(e.FullName)) &&
                                              !ChapterVolumeAssembler.IsMacMetadata(e.FullName));
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                return null;
            }
        }
    }
}
