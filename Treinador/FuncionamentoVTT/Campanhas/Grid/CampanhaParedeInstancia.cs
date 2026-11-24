using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Anexado a cada GameObject de parede instanciado por CampanhaMapManager.InstanciarCadeiaDeParede —
/// guarda quais WallLine (de CampanhaMapaData.paredes) foram encadeados nesse EdgeCollider2D. Uma
/// cadeia pode fundir vários WallLine originais que compartilham ponta (ver AgruparEmCadeias), então
/// o GameObject sozinho não corresponde a um único WallLine — isso é o que permite a WallEditorTools
/// (borracha) remover exatamente os segmentos certos da lista quando o usuário apaga esse GameObject.
/// </summary>
public class CampanhaParedeInstancia : MonoBehaviour
{
    public List<WallLine> LinhasOrigem;
}
