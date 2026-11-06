using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json.Serialization;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Fan translations (2026-11-05): "CBZ (fan translation)" (14) is the quality of a volume taken
    // from a fan-translation release. Every profile gets it as the lowest item, disallowed
    // (AllQualitiesValidator requires every quality on every saved profile), so nothing changes
    // until a user ticks it. No schema change. Idempotent per profile.
    [Migration(064)]
    public class fan_translation_quality : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(InsertFanTranslation);
        }

        private void InsertFanTranslation(IDbConnection conn, IDbTransaction tran)
        {
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<ProfileItem64>>(new QualityIntConverter()));
            var updater = new ProfileUpdater64(conn, tran);

            updater.InsertFanTranslation();

            updater.Commit();
        }

        public class Profile64
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Cutoff { get; set; }
            public List<ProfileItem64> Items { get; set; }
        }

        // Mirrors QualityProfileQualityItem's stored shape: a flat item has "quality"; a group item
        // has "id" + "name" and its qualities in "items" (no "quality" key). Kept whole so the
        // rewrite hands a group back unchanged.
        public class ProfileItem64
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int Id { get; set; }
            public string Name { get; set; }
            public int? Quality { get; set; }
            public bool Allowed { get; set; }
            public List<ProfileItem64> Items { get; set; } = new List<ProfileItem64>();
        }

        public class ProfileUpdater64
        {
            private readonly IDbConnection _connection;
            private readonly IDbTransaction _transaction;

            private readonly List<Profile64> _profiles;
            private readonly HashSet<Profile64> _changedProfiles = new HashSet<Profile64>();

            public ProfileUpdater64(IDbConnection conn, IDbTransaction tran)
            {
                _connection = conn;
                _transaction = tran;

                _profiles = _connection.Query<Profile64>(@"SELECT ""Id"", ""Name"", ""Cutoff"", ""Items"" FROM ""QualityProfiles""",
                    transaction: _transaction).ToList();
            }

            public void Commit()
            {
                var sql = "UPDATE \"QualityProfiles\" SET \"Name\" = @Name, \"Cutoff\" = @Cutoff, \"Items\" = @Items WHERE \"Id\" = @Id";
                _connection.Execute(sql, _changedProfiles, transaction: _transaction);

                _changedProfiles.Clear();
            }

            public void InsertFanTranslation()
            {
                foreach (var profile in _profiles)
                {
                    // Already listed, flat or inside a group (a group item's own Quality is null).
                    if (profile.Items.Any(v => v.Quality == 14 || v.Items.Any(i => i.Quality == 14)))
                    {
                        continue;
                    }

                    // Index 0 is the lowest rank: a fan copy never beats an official one.
                    profile.Items.Insert(0, new ProfileItem64
                    {
                        Quality = 14,
                        Allowed = false
                    });

                    _changedProfiles.Add(profile);
                }
            }
        }
    }
}
