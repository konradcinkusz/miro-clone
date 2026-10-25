using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiroClone.BoardsService.Boards;

namespace MiroClone.BoardsService.Persistence.Configurations;

/// <summary>
/// Columns are snake_case and named explicitly, so the check constraint reads the same on PostgreSQL (which folds
/// unquoted identifiers) and on SQLite.
/// </summary>
internal sealed class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable("boards");
        builder.HasKey(board => board.Id);

        builder.Property(board => board.Id).HasColumnName("id");
        builder.Property(board => board.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        builder.Property(board => board.Title).HasColumnName("title").HasMaxLength(BoardTitle.MaxLength).IsRequired();
        builder.Property(board => board.TitleSearch)
            .HasColumnName("title_search")
            .HasMaxLength(BoardTitle.MaxLength)
            .IsRequired();
        builder.Property(board => board.AuthorId).HasColumnName("author_id").IsRequired();
        builder.Property(board => board.AuthorName)
            .HasColumnName("author_name")
            .HasMaxLength(BoardCaller.AuthorNameMaxLength)
            .IsRequired();
        builder.Property(board => board.ImageSeed).HasColumnName("image_seed").IsRequired();
        builder.Property(board => board.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.ToTable(table => table.HasCheckConstraint("ck_boards_image_seed", "image_seed BETWEEN 1 AND 10"));

        // Every list starts with the workspace and orders by newest first, so one index serves the dashboard.
        builder.HasIndex(board => new { board.WorkspaceId, board.CreatedAt }).HasDatabaseName("ix_boards_workspace_created");
    }
}
