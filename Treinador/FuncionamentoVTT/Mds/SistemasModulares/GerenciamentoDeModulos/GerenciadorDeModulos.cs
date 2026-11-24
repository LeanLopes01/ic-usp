using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;

/// <summary>
/// Ponto de entrada do sistema de Data Packs (ver dicevtt-gerenciamento-modulos.md): varre
/// /Modules ao iniciar, indexa cada módulo de forma LEVE (só id/nome/caminho por categoria) e só
/// abre o XML completo de um item quando ele é de fato selecionado — Lazy Loading, evita carregar
/// centenas de raças/classes/magias/itens de uma vez e travar a thread principal.
///
/// Fica em Mds/SistemasModulares/GerenciamentoDeModulos por enquanto — ainda NÃO substitui
/// TelaInicial/Sistemas/SistemasListManager.cs (sistema de "sistema homebrew" atual). A integração
/// fica pra depois, por pedido explícito do usuário.
/// </summary>
public class GerenciadorDeModulos : MonoBehaviour
{
    public static GerenciadorDeModulos Instancia { get; private set; }

    // Cada categoria mapeia pro nome da subpasta dentro do módulo E pro nome do elemento XML raiz
    // de cada arquivo dela (ex: pasta "Spells" contém arquivos <Spell id="..." name="..."> por
    // magia) — usado tanto pra listar os arquivos quanto pra achar o atributo id/name ao indexar.
    private static readonly Dictionary<string, string> CategoriasESeusElementosRaiz = new Dictionary<string, string>
    {
        { "Races",   "Race" },
        { "Classes", "Class" },
        { "Spells",  "Spell" },
        { "Items",   "Item" },
    };

    private readonly Dictionary<string, ManifestoModulo> _modulosCarregados =
        new Dictionary<string, ManifestoModulo>();

    // Índice[moduloId][categoria] = lista leve de entradas
    private readonly Dictionary<string, Dictionary<string, List<EntradaIndice>>> _indices =
        new Dictionary<string, Dictionary<string, List<EntradaIndice>>>();

    private string _pastaModulos;

    private void Awake()
    {
        if (Instancia != null && Instancia != this) { Destroy(gameObject); return; }
        Instancia = this;

        _pastaModulos = Path.Combine(Application.persistentDataPath, "Modules");
        if (!Directory.Exists(_pastaModulos))
            Directory.CreateDirectory(_pastaModulos);
    }

    // ── Descoberta e indexação ──────────────────────────────────────────────

    /// <summary>Varre /Modules e indexa (leve) todos os módulos encontrados — chamar uma vez no
    /// boot. Cada subpasta de /Modules com um manifest.json válido vira um módulo disponível.</summary>
    public void DescobrirEIndexarTodos()
    {
        _modulosCarregados.Clear();
        _indices.Clear();

        foreach (string pastaModulo in Directory.GetDirectories(_pastaModulos))
        {
            string idModulo = Path.GetFileName(pastaModulo);
            if (!TentarCarregarManifesto(pastaModulo, out ManifestoModulo manifesto)) continue;

            _modulosCarregados[idModulo] = manifesto;
            IndexarModulo(idModulo, pastaModulo);
        }
    }

    private bool TentarCarregarManifesto(string pastaModulo, out ManifestoModulo manifesto)
    {
        manifesto = null;
        string caminhoManifesto = Path.Combine(pastaModulo, "manifest.json");
        if (!File.Exists(caminhoManifesto)) return false;

        try
        {
            manifesto = JsonUtility.FromJson<ManifestoModulo>(File.ReadAllText(caminhoManifesto));
            return manifesto != null && !string.IsNullOrWhiteSpace(manifesto.id);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GerenciadorDeModulos] manifest.json inválido em '{pastaModulo}': {e.Message}");
            return false;
        }
    }

    // Indexação LEVE: lê só o primeiro elemento raiz de cada arquivo (id/name) via XmlReader —
    // nunca materializa a árvore inteira do XML aqui, isso só acontece em CarregarItemCompleto
    // (Lazy Load, sob demanda).
    private void IndexarModulo(string idModulo, string pastaModulo)
    {
        var indicePorCategoria = new Dictionary<string, List<EntradaIndice>>();

        foreach (KeyValuePair<string, string> par in CategoriasESeusElementosRaiz)
        {
            string categoria = par.Key;
            string elementoRaiz = par.Value;
            string pastaCategoria = Path.Combine(pastaModulo, categoria);
            if (!Directory.Exists(pastaCategoria)) continue;

            var entradas = new List<EntradaIndice>();
            foreach (string arquivo in Directory.GetFiles(pastaCategoria, "*.xml"))
            {
                EntradaIndice entrada = LerCabecalhoLeve(arquivo, elementoRaiz);
                if (entrada != null) entradas.Add(entrada);
            }
            indicePorCategoria[categoria] = entradas;
        }

        _indices[idModulo] = indicePorCategoria;
    }

    private static EntradaIndice LerCabecalhoLeve(string arquivo, string nomeElementoRaiz)
    {
        try
        {
            using (XmlReader leitor = XmlReader.Create(arquivo))
            {
                while (leitor.Read())
                {
                    if (leitor.NodeType != XmlNodeType.Element || leitor.Name != nomeElementoRaiz) continue;

                    return new EntradaIndice
                    {
                        id = leitor.GetAttribute("id"),
                        nome = leitor.GetAttribute("name"),
                        caminhoArquivo = arquivo,
                    };
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GerenciadorDeModulos] Falha ao indexar '{arquivo}': {e.Message}");
        }
        return null;
    }

    // ── Consulta ─────────────────────────────────────────────────────────────

    public IReadOnlyList<string> ObterModulosDisponiveis() => new List<string>(_modulosCarregados.Keys);

    public ManifestoModulo ObterManifesto(string idModulo) =>
        _modulosCarregados.TryGetValue(idModulo, out ManifestoModulo manifesto) ? manifesto : null;

    public IReadOnlyList<EntradaIndice> ObterIndiceDaCategoria(string idModulo, string categoria)
    {
        if (_indices.TryGetValue(idModulo, out Dictionary<string, List<EntradaIndice>> porCategoria) &&
            porCategoria.TryGetValue(categoria, out List<EntradaIndice> lista))
            return lista;
        return new List<EntradaIndice>();
    }

    /// <summary>Lazy Load: só abre e materializa o XML completo do item quando ele é de fato
    /// selecionado (ex: usuário clicou em "Bola de Fogo" na lista) — nunca no boot.</summary>
    public XmlDocument CarregarItemCompleto(string idModulo, string categoria, string idItem)
    {
        EntradaIndice entrada = null;
        foreach (EntradaIndice e in ObterIndiceDaCategoria(idModulo, categoria))
        {
            if (e.id != idItem) continue;
            entrada = e;
            break;
        }

        if (entrada == null || !File.Exists(entrada.caminhoArquivo))
        {
            Debug.LogError($"[GerenciadorDeModulos] Lazy Load falhou: '{categoria}:{idItem}' não encontrado em '{idModulo}'.");
            return null;
        }

        var documento = new XmlDocument();
        documento.Load(entrada.caminhoArquivo);
        return documento;
    }
}
