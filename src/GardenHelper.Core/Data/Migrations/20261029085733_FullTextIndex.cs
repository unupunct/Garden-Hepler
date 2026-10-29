using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GardenHelper.Core.Data.Migrations
{
    /// <summary>
    /// SQLite FTS5 index over library text. Diacritics are removed so "samanta" finds "sămânță".
    /// External-content table kept in sync with triggers.
    /// </summary>
    public partial class FullTextIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE VIRTUAL TABLE BookSectionFts USING fts5(
    Title, Text,
    content='BookSections', content_rowid='Id',
    tokenize='unicode61 remove_diacritics 2');");
            migrationBuilder.Sql(@"
CREATE TRIGGER BookSections_ai AFTER INSERT ON BookSections BEGIN
    INSERT INTO BookSectionFts(rowid, Title, Text) VALUES (new.Id, new.Title, new.Text);
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER BookSections_ad AFTER DELETE ON BookSections BEGIN
    INSERT INTO BookSectionFts(BookSectionFts, rowid, Title, Text) VALUES ('delete', old.Id, old.Title, old.Text);
END;");
            migrationBuilder.Sql(@"
CREATE TRIGGER BookSections_au AFTER UPDATE ON BookSections BEGIN
    INSERT INTO BookSectionFts(BookSectionFts, rowid, Title, Text) VALUES ('delete', old.Id, old.Title, old.Text);
    INSERT INTO BookSectionFts(rowid, Title, Text) VALUES (new.Id, new.Title, new.Text);
END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS BookSections_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS BookSections_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS BookSections_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS BookSectionFts;");
        }
    }
}
