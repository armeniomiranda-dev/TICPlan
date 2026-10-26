using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;

namespace AppTIC.Web.Services;

public interface IActivityLog
{
    Task LogAsync(string categoria, string acao, string mensagem, NivelRegisto nivel = NivelRegisto.Info,
        int? gtfsFeedId = null, string? ownerId = null, string? userName = null);
}

public class ActivityLog(ApplicationDbContext db, ICurrentUser currentUser, ILogger<ActivityLog> logger) : IActivityLog
{
    public async Task LogAsync(string categoria, string acao, string mensagem, NivelRegisto nivel = NivelRegisto.Info,
        int? gtfsFeedId = null, string? ownerId = null, string? userName = null)
    {
        var owner = ownerId ?? currentUser.UserId;
        if (owner is null)
        {
            logger.LogWarning("Registo sem utilizador: {Categoria} {Acao} {Mensagem}", categoria, acao, mensagem);
            return;
        }

        db.Registos.Add(new RegistoAtividade
        {
            OwnerId = owner,
            Data = DateTime.UtcNow,
            Nivel = nivel,
            Categoria = categoria,
            Acao = acao,
            Mensagem = mensagem,
            GtfsFeedId = gtfsFeedId,
            UserName = userName ?? currentUser.UserName,
            EnderecoIp = currentUser.IpAddress
        });
        await db.SaveChangesAsync();
    }
}
