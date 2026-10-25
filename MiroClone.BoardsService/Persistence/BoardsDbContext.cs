using Microsoft.EntityFrameworkCore;

namespace MiroClone.BoardsService.Persistence;

/// <summary>The boards database. Entities (Board, BoardFavourite) arrive with WP04; this context starts empty.</summary>
public sealed class BoardsDbContext(DbContextOptions<BoardsDbContext> options) : DbContext(options);
