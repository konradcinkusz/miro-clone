using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiroClone.BoardsService.Boards;

namespace MiroClone.BoardsService.Persistence.Configurations;

internal sealed class BoardFavouriteConfiguration : IEntityTypeConfiguration<BoardFavourite>
{
    public void Configure(EntityTypeBuilder<BoardFavourite> builder)
    {
        builder.ToTable("board_favourites");
        builder.HasKey(favourite => new { favourite.UserId, favourite.BoardId });

        builder.Property(favourite => favourite.UserId).HasColumnName("user_id");
        builder.Property(favourite => favourite.BoardId).HasColumnName("board_id");

        // Deleting a board removes its favourites in the database as well as in the service's delete path.
        builder.HasOne(favourite => favourite.Board)
            .WithMany(board => board.Favourites)
            .HasForeignKey(favourite => favourite.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(favourite => favourite.UserId).HasDatabaseName("ix_board_favourites_user");
    }
}
