using System.Globalization;
using System.Xml;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Motor de UI Declarativa (ver dicevtt-ui-declarativa.md): monta a interface de criação/ficha
/// recursivamente a partir de um XML de layout, em vez de C# com switch fixo amarrado a Prefabs
/// específicos por sistema de jogo. Cada sistema de RPG (D&D 5e, Pathfinder, homebrew) só declara
/// seu próprio XML de layout — sem recompilar nada — reaproveitando os MESMOS micro-prefabs
/// atômicos (Label, InputNumber, AttributeRow, ...) configurados no Inspector.
///
/// Fica em Mds/SistemasModulares/UIDeclarativa por enquanto — ainda NÃO substitui nenhuma tela de
/// TelaInicial/Sistemas. A integração fica pra depois, por pedido explícito do usuário.
/// </summary>
public class ConstrutorDeUI : MonoBehaviour
{
    [Header("Containers e Layouts")]
    [SerializeField] private GameObject prefabGrid;
    [SerializeField] private GameObject prefabVerticalLayout;
    [SerializeField] private GameObject prefabHorizontalLayout;
    [SerializeField] private GameObject prefabSecao;

    [Header("Componentes Atômicos")]
    [SerializeField] private GameObject prefabLabel;
    [SerializeField] private GameObject prefabCampoNumerico;
    [SerializeField] private GameObject prefabLinhaAtributo;

    private EstadoPersonagem _estado;

    /// <summary>Ponto de entrada — monta a árvore inteira dentro de containerPai a partir dos
    /// filhos de nodeRaiz (ex: o próprio &lt;UserInterface&gt;).</summary>
    public void ConstruirInterface(XmlNode nodeRaiz, Transform containerPai, EstadoPersonagem estado)
    {
        _estado = estado;
        foreach (XmlNode filho in nodeRaiz.ChildNodes)
            ConstruirElemento(filho, containerPai);
    }

    private void ConstruirElemento(XmlNode node, Transform pai)
    {
        if (node.NodeType != XmlNodeType.Element) return;

        GameObject instancia = null;

        switch (node.Name.ToLowerInvariant())
        {
            case "grid":
                instancia = InstanciarSeConfigurado(prefabGrid, pai);
                ConfigurarGrid(node, instancia);
                break;

            case "verticallayout":
                instancia = InstanciarSeConfigurado(prefabVerticalLayout, pai);
                break;

            case "horizontallayout":
                instancia = InstanciarSeConfigurado(prefabHorizontalLayout, pai);
                break;

            case "section":
                instancia = InstanciarSeConfigurado(prefabSecao, pai);
                instancia?.GetComponent<SecaoUI>()?.DefinirTitulo(node.Attributes?["title"]?.Value);
                break;

            case "label":
                instancia = InstanciarSeConfigurado(prefabLabel, pai);
                ConfigurarLabel(node, instancia);
                break;

            case "inputnumber":
                instancia = InstanciarSeConfigurado(prefabCampoNumerico, pai);
                instancia?.GetComponent<CampoNumericoReativo>()?.Configurar(
                    node.Attributes?["bind"]?.Value, _estado,
                    ObterFloatAttr(node, "min", 0f), ObterFloatAttr(node, "max", float.MaxValue));
                break;

            case "attributerow":
                instancia = InstanciarSeConfigurado(prefabLinhaAtributo, pai);
                instancia?.GetComponent<LinhaAtributoReativa>()?.Configurar(
                    node.Attributes?["label"]?.Value,
                    node.Attributes?["bind_val"]?.Value,
                    node.Attributes?["bind_mod"]?.Value,
                    _estado);
                break;

            // ListView (inventário etc.) fica fora de escopo aqui — precisa de um data_source
            // dinâmico por sistema que ainda não existe (ver dicevtt-ui-declarativa.md §2, coluna
            // "Inventário"); o nó só é ignorado por enquanto em vez de quebrar o parse do resto da
            // árvore.
            default:
                break;
        }

        if (instancia == null || !node.HasChildNodes) return;

        Transform containerInterno = instancia.GetComponent<ContainerDeLayout>()?.ContentParent ?? instancia.transform;
        foreach (XmlNode filho in node.ChildNodes)
            ConstruirElemento(filho, containerInterno);
    }

    private static GameObject InstanciarSeConfigurado(GameObject prefab, Transform pai)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[ConstrutorDeUI] Prefab não configurado no Inspector pra um dos elementos do XML de layout.");
            return null;
        }
        return UIUtils.Spawn(prefab, pai, false);
    }

    private void ConfigurarLabel(XmlNode node, GameObject instancia)
    {
        LabelReativo label = instancia?.GetComponent<LabelReativo>();
        if (label == null) return;

        string bind = node.Attributes?["bind"]?.Value;
        if (!string.IsNullOrEmpty(bind))
            label.VincularPropriedade(bind, _estado);
        else
            label.DefinirTexto(node.Attributes?["text"]?.Value ?? string.Empty);
    }

    private static void ConfigurarGrid(XmlNode node, GameObject gridObj)
    {
        GridLayoutGroup grid = gridObj?.GetComponent<GridLayoutGroup>();
        if (grid == null) return;

        if (int.TryParse(node.Attributes?["columns"]?.Value, out int colunas) && colunas > 0)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = colunas;
        }
    }

    private static float ObterFloatAttr(XmlNode node, string nomeAtributo, float valorPadrao)
    {
        string valor = node.Attributes?[nomeAtributo]?.Value;
        return float.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out float resultado)
            ? resultado : valorPadrao;
    }
}
