using Microsoft.EntityFrameworkCore;

namespace MiroClone.CanvasService.Persistence;

/// <summary>The canvas database. The JSON snapshot table arrives with WP05; this context starts empty.</summary>
public sealed class CanvasDbContext(DbContextOptions<CanvasDbContext> options) : DbContext(options);
