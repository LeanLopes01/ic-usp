using UnityEngine;

/// <summary>Marca um GameObject de layout (Grid/VerticalLayout/HorizontalLayout/Section) cujo
/// "container de conteúdo" real não é a raiz do prefab — ex: uma Section pode ter borda/título
/// fora da área onde os filhos declarados no XML devem ser instanciados. Se ausente, ConstrutorDeUI
/// usa a própria raiz do prefab como container (ver ConstrutorDeUI.ConstruirElemento).</summary>
public class ContainerDeLayout : MonoBehaviour
{
    [SerializeField] private Transform contentParent;
    public Transform ContentParent => contentParent != null ? contentParent : transform;
}
