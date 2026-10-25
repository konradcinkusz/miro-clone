using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiroClone.BoardsService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Boards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title_search = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    image_seed = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boards", x => x.id);
                    table.CheckConstraint("ck_boards_image_seed", "image_seed BETWEEN 1 AND 10");
                });

            migrationBuilder.CreateTable(
                name: "board_favourites",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_board_favourites", x => new { x.user_id, x.board_id });
                    table.ForeignKey(
                        name: "FK_board_favourites_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_board_favourites_board_id",
                table: "board_favourites",
                column: "board_id");

            migrationBuilder.CreateIndex(
                name: "ix_board_favourites_user",
                table: "board_favourites",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_boards_workspace_created",
                table: "boards",
                columns: new[] { "workspace_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "board_favourites");

            migrationBuilder.DropTable(
                name: "boards");
        }
    }
}
