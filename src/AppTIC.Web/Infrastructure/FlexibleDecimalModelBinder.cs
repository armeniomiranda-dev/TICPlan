using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AppTIC.Web.Infrastructure;

/// <summary>Aceita "41,1496" e "41.1496" em campos decimais (coordenadas copiadas de mapas usam ponto).</summary>
public class FlexibleDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext ctx)
    {
        var value = ctx.ValueProvider.GetValue(ctx.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        ctx.ModelState.SetModelValue(ctx.ModelName, value);

        var raw = value.FirstValue?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            if (Nullable.GetUnderlyingType(ctx.ModelType) is not null) ctx.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        if (decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            ctx.Result = ModelBindingResult.Success(d);
        else
            ctx.ModelState.TryAddModelError(ctx.ModelName, $"\"{raw}\" não é um número válido.");
        return Task.CompletedTask;
    }
}

public class FlexibleDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.ModelType == typeof(decimal) || context.Metadata.ModelType == typeof(decimal?)
            ? new FlexibleDecimalModelBinder()
            : null;
}
