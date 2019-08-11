using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Karambolo.Common;
using Unity.NetCode;
using UnityEditor;
using UnityEngine;

public class GenerateAdditionalRpcAttribute : Attribute
{}

public class RpcCollectionGeneratorWindow : EditorWindow
{
    private const string RpcCollectionTemplate = @"using System;
using Unity.Entities;
using Unity.Networking.Transport;
using Unity.NetCode;

public struct /*$RPC_COLLECTION_PREFIX*/RpcCollection : IRpcCollection
{
    static Type[] s_RpcTypes = new Type[]
    {
/*$RPC_TYPE_LIST*/
    };
    public void ExecuteRpc(int type, DataStreamReader reader, ref DataStreamReader.Context ctx, Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex)
    {
        switch (type)
        {
/*$RPC_TYPE_CASES*/
        }
    }

    public int GetRpcFromType<T>() where T : struct, IRpcCommand
    {
        for (int i = 0; i < s_RpcTypes.Length; ++i)
        {
            if (s_RpcTypes[i] == typeof(T))
                return i;
        }

        return -1;
    }
}

public class /*$RPC_SYSTEM_PREFIX*/RpcSystem : RpcSystem</*$RPC_COLLECTION_PREFIX*/RpcCollection>
{
    protected override void OnCreate()
    {
        base.OnCreate();

/*$RPC_REGISTER_SYSTEMS*/
    }
}
";

    private const string RpcCaseTemplate = @"            case /*$RPC_CASE_NUM*/:
            {
                var tmp = new /*$RPC_CASE_TYPE*/();
                tmp.Deserialize(reader, ref ctx);
                tmp.Execute(connection, commandBuffer, jobIndex);
                break;
            }
";

    private const string RpcTypeTemplate = @"        typeof(/*$RPC_TYPE*/),
";

    private const string RpcRegisterSystemTemplate = @"        World.GetOrCreateSystem<RpcQueueSystem</*$RPC_TYPE*/>>().SetTypeIndex(/*$RPC_TYPE_INDEX*/);
";

    [MenuItem("Multiplayer/CodeGen/RpcCollection Generator")]
    public static void ShowWindow()
    {
        GetWindow<RpcCollectionGeneratorWindow>(false, "RpcCollection Generator", true);
    }

    class RpcType
    {
        public Type type;
        public bool generate;
    }

    private List<RpcType> m_RpcTypes;

    public RpcCollectionGeneratorWindow()
    {
        m_RpcTypes = new List<RpcType>();
    }

    public static string SpecifiedTypeName(Type type)
    {
        return SpecifiedTypeName(type, new Queue<Type>(type.GetGenericArguments()));
    }

    private static string SpecifiedTypeName(Type type, Queue<Type> args)
    {
        var name = type.Name;
        if (type.IsGenericParameter)
        {
            return name;
        }

        if (type.IsNested)
        {
            name = $"{SpecifiedTypeName(type.DeclaringType, args)}.{name}";
        }

        if (type.IsGenericType)
        {
            var tickIndex = name.IndexOf('`');
            if (tickIndex > -1)
                name = name.Remove(tickIndex);
            var genericTypes = type.GetGenericArguments();

            var genericTypeNames = new StringBuilder();
            for (var i = 0; i < genericTypes.Length && args.Count > 0; i++)
            {
                if (i != 0)
                    genericTypeNames.Append(", ");
                genericTypeNames.Append(SpecifiedTypeName(args.Dequeue()));
            }

            if (genericTypeNames.Length > 0)
            {
                name = $"{name}<{genericTypeNames}>";
            }
        }

        return name;
    }

    private void OnGUI()
    {
        if (GUILayout.Button("Scan for Rpcs"))
        {
            FindAllRpcs();
        }

        for (int i = 0; i < m_RpcTypes.Count; ++i)
        {
            m_RpcTypes[i].generate = GUILayout.Toggle(m_RpcTypes[i].generate, SpecifiedTypeName(m_RpcTypes[i].type));
        }

        if (GUILayout.Button("Generate Collection"))
        {
            var dstFile = EditorUtility.SaveFilePanel("Select file to save", "", "RpcCollection", "cs");

            string rpcCases           = "";
            string rpcTypes           = "";
            string rpcRegisterSystems = "";
            for (int i = 0; i < m_RpcTypes.Count; ++i)
            {
                if (m_RpcTypes[i].generate)
                {
                    rpcCases += RpcCaseTemplate
                                .Replace("/*$RPC_CASE_NUM*/", i.ToString())
                                .Replace("/*$RPC_CASE_TYPE*/", SpecifiedTypeName(m_RpcTypes[i].type));
                    rpcTypes += RpcTypeTemplate.Replace("/*$RPC_TYPE*/", SpecifiedTypeName(m_RpcTypes[i].type));
                    rpcRegisterSystems += RpcRegisterSystemTemplate
                                          .Replace("/*$RPC_TYPE*/", SpecifiedTypeName(m_RpcTypes[i].type))
                                          .Replace("/*$RPC_TYPE_INDEX*/", i.ToString());
                }
            }

            string content = RpcCollectionTemplate
                             .Replace("/*$RPC_TYPE_CASES*/", rpcCases)
                             .Replace("/*$RPC_TYPE_LIST*/", rpcTypes)
                             .Replace("/*$RPC_COLLECTION_PREFIX*/", "")
                             .Replace("/*$RPC_SYSTEM_PREFIX*/", Application.productName)
                             .Replace("/*$RPC_REGISTER_SYSTEMS*/", rpcRegisterSystems);
            File.WriteAllText(dstFile, content);
        }
    }

    void FindAllRpcs()
    {
        void FindNestedRpcTypes(Type t, List<Type> list)
        {
            var b = t.BaseType;
            if (b == null)
                return;

            foreach (var nested in b.GetNestedTypes())
            {
                if (typeof(IRpcCommand).IsAssignableFrom(nested))
                {
                    list.Add(nested.MakeGenericType(b.GenericTypeArguments[0]));
                }

                FindNestedRpcTypes(nested, list);
            }
        }

        m_RpcTypes.Clear();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            IEnumerable<Type> allTypes;

            try
            {
                allTypes = assembly.GetTypes();

            }
            catch (ReflectionTypeLoadException e)
            {
                allTypes = e.Types.Where(t => t != null);
                Debug.LogWarning(
                    $"RpcCollectionGenerator failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
            }

            var pipelineTypes = allTypes.Where(t =>
                typeof(IRpcCommand).IsAssignableFrom(t) &&
                !t.IsAbstract && t.IsPublic &&
                !t.ContainsGenericParameters);

            var nestedTypeList = new List<Type>();
            foreach (var t in allTypes)
            {
                FindNestedRpcTypes(t, nestedTypeList);
            }

            pipelineTypes = pipelineTypes.Concat(nestedTypeList);
            
            var additionalTypes = new List<Type>();
            foreach (var t in allTypes)
            {
                foreach (var method in t.GetMethods())
                {
                    if (!method.HasAttribute<GenerateAdditionalRpcAttribute>())
                        continue;
                    method.Invoke(null, new object[] {additionalTypes});
                }
            }

            pipelineTypes = pipelineTypes.Concat(additionalTypes);

            foreach (var pt in pipelineTypes)
            {
                Debug.Log(pt.FullName);
                m_RpcTypes.Add(new RpcType {type = pt, generate = true});
            }
        }
    }
}