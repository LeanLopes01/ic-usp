using System;
using System.Collections.Generic;

/// <summary>
/// Metadados de identidade do sistema, lidos de dados/regras_jogando.json.
/// Formato objeto: { "nome": "...", "descricao": "..." }
/// </summary>
[Serializable]
public class SistemaMetadados
{
    public string nome;
    public string descricao;
    public string versao;
    public string autor;
}

/// <summary>
/// Nível mais profundo: passo numerado dentro de um subtópico.
/// Campos: passo, nome, descricao
/// </summary>
[Serializable]
public class PassoRegra
{
    public int    passo;
    public string nome;
    public string descricao;
}

/// <summary>
/// Segundo nível: subtópico de uma regra principal.
/// Campos: id, topico, descricao, subtopicos (lista de PassoRegra)
/// </summary>
[Serializable]
public class SubtopicoRegra
{
    public string         id;
    public string         topico;
    public string         descricao;
    public List<PassoRegra> subtopicos;
}

/// <summary>
/// Item de topo quando regras_jogando.json está no formato de array de regras.
/// Campos: id, topico, descricao, subtopicos (lista de SubtopicoRegra)
/// </summary>
[Serializable]
public class RegraJogandoItem
{
    public string              id;
    public string              topico;
    public string              descricao;
    public List<SubtopicoRegra> subtopicos;
}

/// <summary>
/// Um bloco de tópico livre (título + corpo) dentro das regras gerais do sistema.
/// </summary>
[Serializable]
public class TopicoRegra
{
    public string titulo;
    public string conteudo;

    public TopicoRegra(string titulo = "", string conteudo = "")
    {
        this.titulo   = titulo;
        this.conteudo = conteudo;
    }
}

/// <summary>
/// Raiz do JSON de regras gerais — lista de tópicos livres definidos pelo GM.
/// </summary>
[Serializable]
public class SystemRulesData
{
    public List<TopicoRegra> topicos = new List<TopicoRegra>();
}
