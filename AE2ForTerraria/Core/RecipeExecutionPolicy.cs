using System.Reflection;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.ModLoader;

namespace AE2ForTerraria.Core;

/// <summary>Do not silently bypass callbacks whose effects cannot be represented by a storage transaction.</summary>
public static class RecipeExecutionPolicy
{
    private static readonly PropertyInfo CraftHook=typeof(Recipe).GetProperty("OnCraftHooks",BindingFlags.Instance|BindingFlags.NonPublic);
    private static readonly PropertyInfo ConsumeHook=typeof(Recipe).GetProperty("ConsumeIngredientHooks",BindingFlags.Instance|BindingFlags.NonPublic);
    private sealed class Result { public string Reason; }
    private static readonly ConditionalWeakTable<Recipe,Result> Cache=new();
    public static string BlockReason(Recipe recipe) => Cache.GetValue(recipe,Inspect).Reason;
    private static Result Inspect(Recipe recipe)
    {
        string reason=null;
        if (CraftHook==null || ConsumeHook==null) reason="当前 tML 的合成接口无法核验";
        else if (CraftHook.GetValue(recipe)!=null || ConsumeHook.GetValue(recipe)!=null)
            reason="此配方含特殊合成或消耗规则，请手动制作";
        else if (recipe.createItem.ModItem is ModItem item
            && item.GetType().GetMethod(nameof(ModItem.OnCreated))?.DeclaringType!=typeof(ModItem))
            reason="此物品有特殊创建规则，请手动制作";
        return new Result { Reason=reason };
    }
}
