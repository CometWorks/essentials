using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sandbox.Game.Entities.Cube;

namespace ServerPlugin.PcuTransfer;

public static class BlockLimitsPluginBridge
{
    public static bool IsAvailableAndEnabled()
    {
        Type apiType = FindApiType();
        if (apiType == null)
            return false;

        PropertyInfo enabledProperty = apiType.GetProperty(
            "Enabled",
            BindingFlags.Public | BindingFlags.Static
        );
        if (enabledProperty?.PropertyType == typeof(bool))
            return (bool)enabledProperty.GetValue(null);

        MethodInfo enabledMethod = apiType.GetMethod(
            "IsEnabled",
            BindingFlags.Public | BindingFlags.Static,
            null,
            Type.EmptyTypes,
            null
        );
        if (enabledMethod?.ReturnType == typeof(bool))
            return (bool)enabledMethod.Invoke(null, null);

        return true;
    }

    public static bool TryCanAdd(
        List<MySlimBlock> blocks,
        long identityId,
        out bool allowed,
        out List<MySlimBlock> deniedBlocks
    )
    {
        allowed = true;
        deniedBlocks = null;

        Type apiType = FindApiType();
        if (apiType == null)
            return false;

        MethodInfo canAdd = apiType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
            {
                if (method.Name != "CanAdd" || method.ReturnType != typeof(bool))
                    return false;

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(List<MySlimBlock>)
                    && parameters[1].ParameterType == typeof(long)
                    && parameters[2].IsOut;
            });

        if (canAdd == null)
            return false;

        object[] args = { blocks, identityId, null };
        allowed = (bool)canAdd.Invoke(null, args);
        deniedBlocks = args[2] as List<MySlimBlock> ?? new List<MySlimBlock>();
        return true;
    }

    private static Type FindApiType() =>
        AppDomain
            .CurrentDomain.GetAssemblies()
            .Select(assembly =>
                assembly.GetType("BlockLimits.PluginApi.Limits", throwOnError: false)
            )
            .FirstOrDefault(type => type != null);
}
