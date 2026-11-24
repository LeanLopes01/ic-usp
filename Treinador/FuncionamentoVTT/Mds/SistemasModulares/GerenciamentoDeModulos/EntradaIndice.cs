using System;

/// <summary>Entrada leve do índice de um módulo (ver GerenciadorDeModulos.IndexarModulo) — só o
/// necessário pra popular uma lista de seleção sem abrir o XML completo do item (Lazy Loading, ver
/// dicevtt-gerenciamento-modulos.md §2/§3).</summary>
[Serializable]
public class EntradaIndice
{
    public string id;
    public string nome;
    public string caminhoArquivo;
}
