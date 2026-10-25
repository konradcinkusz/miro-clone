using Microsoft.EntityFrameworkCore;
using MiroClone.BoardsService.Boards;

namespace MiroClone.BoardsService.Persistence;

/// <summary>The boards database: boards and per-user favourites. Schema changes arrive only as migrations (P4).</summary>
public sealed class BoardsDbContext(DbContextOptions<BoardsDbContext> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();

    public DbSet<BoardFavourite> BoardFavourites => Set<BoardFavourite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BoardsDbContext).Assembly);
}
