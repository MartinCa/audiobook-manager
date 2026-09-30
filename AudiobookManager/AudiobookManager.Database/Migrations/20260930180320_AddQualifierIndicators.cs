using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddQualifierIndicators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "qualifier_indicator",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    source = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    indicator = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    qualifier_key = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qualifier_indicator", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_qualifier_indicator_source_indicator",
                table: "qualifier_indicator",
                columns: new[] { "source", "indicator" },
                unique: true);

            // Audible's own wordings, so the feature works before anything is configured. Plain
            // SQL rather than InsertData so the model snapshot carries no seed rows a later edit
            // by the user would fight.
            migrationBuilder.Sql(
                """
                INSERT INTO qualifier_indicator (source, indicator, qualifier_key) VALUES
                    ('Audible', 'Dramatized Adaptation', 'dramatized'),
                    ('Audible', 'Full-Cast Dramatized Adaptation', 'dramatized'),
                    ('Audible', 'Dramatized', 'dramatized'),
                    ('Audible', 'Abridged', 'abridged');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "qualifier_indicator");
        }
    }
}
