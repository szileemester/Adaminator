using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adaminator.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Restores a database-level guarantee that a tournament's participant names are unique, after
    /// RelaxParticipantNameIndex had to drop the unique index to let a roster save rename two people at
    /// once (swapping two names is a cycle of UPDATEs that no ordering satisfies while the index holds).
    ///
    /// The replacement is DEFERRABLE INITIALLY DEFERRED, so it is checked once at COMMIT rather than
    /// after every row: the swap passes through an invalid intermediate state and lands valid, which is
    /// exactly what the old index could not express. It is also case-insensitive, which the old index
    /// never was - it compared raw text, so "Alice" and "alice" always slipped past it even though the
    /// domain has always rejected them. Postgres cannot put an expression in a UNIQUE *constraint* and
    /// cannot make a unique *index* deferrable, so the lower-cased name is materialised as a generated
    /// column and the constraint is placed on that.
    ///
    /// Written as raw SQL and deliberately left out of the EF model. EF has no concept of a deferrable
    /// constraint, and modelling it as a unique index would bring back the client-side circular-dependency
    /// error that RelaxParticipantNameIndex existed to remove - the swap would fail before a statement
    /// ever reached Postgres. Migrations diff the model against the snapshot, so a column and constraint
    /// EF does not know about are invisible to it and no later migration will try to drop them.
    /// </summary>
    public partial class EnforceParticipantNameUniqueness : Migration
    {
        private const string ConstraintName = "UQ_participants_TournamentId_NameLower";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows predating the domain's case-insensitive rule would make the constraint unaddable, and a
            // migration that cannot apply is a crash loop on boot. The suffix is taken from the row's own
            // id, so the new name is unique by construction - no second pass can collide, unlike a
            // "~2, ~3" counter. 21 + 1 + 8 keeps it inside the 30-character limit on Name.
            migrationBuilder.Sql("""
                UPDATE participants AS p
                SET "Name" = left(p."Name", 21) || '~' || substr(replace(p."Id"::text, '-', ''), 1, 8)
                FROM (
                    SELECT "Id",
                           row_number() OVER (
                               PARTITION BY "TournamentId", lower("Name")
                               ORDER BY "Position", "Id") AS rn
                    FROM participants
                ) AS ranked
                WHERE p."Id" = ranked."Id" AND ranked.rn > 1;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE participants
                ADD COLUMN "NameLower" text GENERATED ALWAYS AS (lower("Name")) STORED;
                """);

            migrationBuilder.Sql($"""
                ALTER TABLE participants
                ADD CONSTRAINT "{ConstraintName}"
                UNIQUE ("TournamentId", "NameLower") DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The renames in Up are not reversed: the original names are not recoverable, and they were
            // duplicates the domain would refuse to save today anyway.
            migrationBuilder.Sql($"""ALTER TABLE participants DROP CONSTRAINT "{ConstraintName}";""");
            migrationBuilder.Sql("""ALTER TABLE participants DROP COLUMN "NameLower";""");
        }
    }
}
