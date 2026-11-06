using System;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Fan translations (2026-11-05): a fan copy is a stop-gap. It is only taken for a volume that has
    // been out for WaitDays (the official release gets its chance first) and never replaces a file.
    public class FanTranslationSpecification : IDecisionEngineSpecification
    {
        public const int WaitDays = 14;

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        // A fan decision never counts as "found" by a search and is grabbed last (2026-11-05).
        public static bool IsFanTranslation(DownloadDecision decision)
        {
            return decision?.RemoteBook?.ParsedBookInfo?.Quality?.Quality == Quality.CbzFanTranslation;
        }

        public Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            if (subject.ParsedBookInfo?.Quality?.Quality != Quality.CbzFanTranslation)
            {
                return Decision.Accept();
            }

            // The wait runs from ReleaseDate as stored: EditionDates.Resolve already stores a month- or
            // year-precision date as the last day of its period, so no coarse-date arithmetic here
            // (rebuilding it from the UTC year pushed a year-precision date a year too late).
            var waitedUntil = DateTime.UtcNow.AddDays(-WaitDays);

            foreach (var book in subject.Books)
            {
                var files = book.EditionOf(subject.MediaType)?.BookFiles?.Value;

                if (files != null && files.Count > 0)
                {
                    return Decision.Reject("A fan translation never replaces an existing file");
                }

                if (!book.ReleaseDate.HasValue)
                {
                    return Decision.Reject("A fan translation is only taken for a released volume, and this one has no release date");
                }

                if (book.ReleaseDate.Value > waitedUntil)
                {
                    return Decision.Reject("A fan translation is only taken {0} days after the volume's release", WaitDays);
                }
            }

            return Decision.Accept();
        }
    }
}
