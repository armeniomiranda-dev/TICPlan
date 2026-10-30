using System.Reflection;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ICurrentUser _currentUser;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    // Lidos pelos filtros globais em cada query. São propriedades (e não campos preenchidos no construtor)
    // porque o contexto pode ser criado pelo Identity antes de a autenticação do pedido estar concluída.
    private string? _userId => _currentUser.UserId;
    private bool _isAdmin => _currentUser.IsAdmin;
    private int? _operadorId => _currentUser.OperadorId;

    public DbSet<Operador> Operadores => Set<Operador>();
    public DbSet<Paragem> Paragens => Set<Paragem>();
    public DbSet<Circulacao> Circulacoes => Set<Circulacao>();
    public DbSet<CirculacaoDia> CirculacaoDias => Set<CirculacaoDia>();
    public DbSet<PercursoParagem> Percursos => Set<PercursoParagem>();
    public DbSet<CirculacaoHorarioTic> CirculacaoHorariosTic => Set<CirculacaoHorarioTic>();
    public DbSet<GtfsFeed> GtfsFeeds => Set<GtfsFeed>();
    public DbSet<RegistoAtividade> Registos => Set<RegistoAtividade>();
    public DbSet<PedidoAlteracao> PedidosAlteracao => Set<PedidoAlteracao>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Tabelas do Identity também em snake_case (asp_net_users, ...), como as restantes.
        foreach (var entity in b.Model.GetEntityTypes())
            if (entity.GetTableName() is { } table && table.StartsWith("AspNet"))
                entity.SetTableName(string.Concat(table.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));

        b.Entity<ApplicationUser>()
            .HasOne(x => x.Operador).WithMany().HasForeignKey(x => x.OperadorId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Operador>(e =>
        {
            e.HasIndex(x => new { x.OwnerId, x.Nif }).IsUnique();
            e.Property(x => x.Ambito).HasConversion<string>().HasMaxLength(30);
        });

        b.Entity<Paragem>(e =>
        {
            e.HasIndex(x => new { x.OwnerId, x.Codigo }).IsUnique();
            e.Property(x => x.Latitude).HasPrecision(9, 6);
            e.Property(x => x.Longitude).HasPrecision(9, 6);
        });

        b.Entity<Circulacao>(e =>
        {
            e.HasIndex(x => new { x.OwnerId, x.ViagemId }).IsUnique();
            e.Property(x => x.Ambito).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Categoria).HasConversion<string>().HasMaxLength(10);
            e.HasOne(x => x.Operador).WithMany(o => o.Circulacoes).HasForeignKey(x => x.OperadorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Origem).WithMany().HasForeignKey(x => x.OrigemId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Destino).WithMany().HasForeignKey(x => x.DestinoId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.GtfsFeed).WithMany().HasForeignKey(x => x.GtfsFeedId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CirculacaoDia>(e =>
        {
            e.HasKey(x => new { x.CirculacaoId, x.Data });
            e.HasIndex(x => x.Data);
            e.HasOne(x => x.Circulacao).WithMany(c => c.Dias).HasForeignKey(x => x.CirculacaoId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _isAdmin || x.Circulacao!.OperadorId == _operadorId);
        });

        b.Entity<PercursoParagem>(e =>
        {
            e.HasKey(x => new { x.CirculacaoId, x.Sequencia });
            e.HasOne(x => x.Circulacao).WithMany(c => c.Percurso).HasForeignKey(x => x.CirculacaoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Paragem).WithMany().HasForeignKey(x => x.ParagemId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _isAdmin || x.Circulacao!.OperadorId == _operadorId);
        });

        b.Entity<CirculacaoHorarioTic>(e =>
        {
            e.ToTable("circulacao_horarios_tic", t => t.HasCheckConstraint("ck_circulacao_horarios_tic_periodo", "data_fim >= data_inicio"));
            e.HasIndex(x => new { x.CirculacaoId, x.DataInicio });
            e.Property(x => x.Origem).HasConversion<string>().HasMaxLength(10);
            e.HasOne(x => x.Circulacao).WithMany(c => c.HorariosTic).HasForeignKey(x => x.CirculacaoId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _isAdmin || x.Circulacao!.OperadorId == _operadorId);
        });

        b.Entity<GtfsFeed>(e =>
        {
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.Sha256);
            e.HasOne(x => x.Operador).WithMany(o => o.GtfsFeeds).HasForeignKey(x => x.OperadorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RegistoAtividade>(e =>
        {
            e.Property(x => x.Nivel).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(x => new { x.OwnerId, x.Data });
            e.HasOne(x => x.GtfsFeed).WithMany(f => f.Registos).HasForeignKey(x => x.GtfsFeedId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PedidoAlteracao>(e =>
        {
            e.ToTable("pedidos_alteracao");
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.Estado, x.CriadoEm });
            e.HasOne(x => x.Circulacao).WithMany().HasForeignKey(x => x.CirculacaoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PedidoPor).WithMany().HasForeignKey(x => x.PedidoPorId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _isAdmin || x.Circulacao!.OperadorId == _operadorId);
        });

        // Segmentação por login: cada entidade com dono só é visível ao próprio dono, ou a um Admin.
        var apply = typeof(ApplicationDbContext).GetMethod(nameof(ApplyOwnerRules), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var type in b.Model.GetEntityTypes().Select(t => t.ClrType).Where(t => typeof(IOwnedEntity).IsAssignableFrom(t)).ToList())
            apply.MakeGenericMethod(type).Invoke(this, [b]);

        // Os utilizadores (não Admin) veem a informação do seu operador, seja qual for o login que a criou:
        // estes filtros substituem o filtro por dono acima. Os registos de atividade continuam por login.
        b.Entity<Operador>().HasQueryFilter(x => _isAdmin || x.Id == _operadorId);
        b.Entity<Circulacao>().HasQueryFilter(x => _isAdmin || x.OperadorId == _operadorId);
        b.Entity<GtfsFeed>().HasQueryFilter(x => _isAdmin || x.OperadorId == _operadorId);
        // Paragens: as usadas nas circulações do operador (origem, destino ou percurso).
        b.Entity<Paragem>().HasQueryFilter(x => _isAdmin
            || Set<Circulacao>().Any(c => c.OrigemId == x.Id || c.DestinoId == x.Id)
            || Set<PercursoParagem>().Any(p => p.ParagemId == x.Id));
    }

    private void ApplyOwnerRules<T>(ModelBuilder b) where T : class, IOwnedEntity
    {
        b.Entity<T>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<T>().HasQueryFilter(x => _isAdmin || x.OwnerId == _userId);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void ApplyOwnership()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IOwnedEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (string.IsNullOrEmpty(entry.Entity.OwnerId))
                    entry.Entity.OwnerId = _userId ?? throw new InvalidOperationException("Não é possível criar registos sem sessão iniciada.");
                else if (!_isAdmin && _userId is not null && entry.Entity.OwnerId != _userId)
                    throw new UnauthorizedAccessException("Não pode criar registos para outro utilizador.");
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                var original = (string?)entry.OriginalValues[nameof(IOwnedEntity.OwnerId)];
                if (!_isAdmin && original != _userId)
                    throw new UnauthorizedAccessException("Não pode alterar registos de outro utilizador.");
                if (!_isAdmin && entry.Entity.OwnerId != original)
                    throw new UnauthorizedAccessException("Não pode transferir registos para outro utilizador.");
            }

            if (entry.Entity is OwnedEntity owned)
            {
                if (entry.State == EntityState.Added) owned.CriadoEm = now;
                if (entry.State is EntityState.Added or EntityState.Modified) owned.AtualizadoEm = now;
            }
        }
    }
}
