// Chapter data (2026-11-06): the chapters a manga volume holds -- "72–81", or "5" for a volume of one
// chapter. Null when the volume has no confirmed chapter list (the API sends chapterFirst /
// chapterLast only for a list the chapter builder would use).
export default function formatChapterSpan(chapterFirst, chapterLast) {
  if (chapterFirst == null) {
    return null;
  }

  return chapterLast == null || chapterLast === chapterFirst ?
    `${chapterFirst}` :
    `${chapterFirst}–${chapterLast}`;
}
