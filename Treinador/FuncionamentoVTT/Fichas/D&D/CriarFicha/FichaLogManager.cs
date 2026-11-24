using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Classe que guarda o momento exato de cada escolha
[Serializable]
public class EscolhaLog
{
    public int nivel;
    public int ordemRegistro;
    public string categoria; // "Raça", "Classe", "Antecedente", "Equipamento"
    public string conteudo;  // O texto completo do que ele ganhou
}

public static class FichaLogManager
{
    public static List<EscolhaLog> historicoEscolhas = new List<EscolhaLog>();
    private static int contadorOrdem = 0;

    /// <summary>ID da ficha cujo log está ativo. 0 = sem ficha.</summary>
    public static int FichaAtiva { get; private set; } = 0;

    /// <summary>
    /// Inicia o log para uma ficha específica, limpando qualquer histórico anterior.
    /// Deve ser chamado sempre que uma ficha é aberta ou criada.
    /// </summary>
    public static void IniciarParaFicha(int fichaId)
    {
        FichaAtiva = fichaId;
        historicoEscolhas.Clear();
        contadorOrdem = 0;
    }

    /// <summary>Limpa completamente o log e desvincula de qualquer ficha.</summary>
    public static void LimparHistorico()
    {
        FichaAtiva = 0;
        historicoEscolhas.Clear();
        contadorOrdem = 0;
    }

    /// <summary>Registra uma escolha no log da ficha ativa.</summary>
    public static void RegistrarEscolha(int nivel, string categoria, string conteudo)
    {
        historicoEscolhas.Add(new EscolhaLog
        {
            nivel = nivel,
            ordemRegistro = contadorOrdem++,
            categoria = categoria,
            conteudo = conteudo
        });
    }

    /// <summary>Retorna o histórico ordenado por nível e ordem de registro.</summary>
    public static List<EscolhaLog> ObterHistoricoOrdenado()
    {
        return historicoEscolhas
            .OrderBy(log => log.nivel)
            .ThenBy(log => log.ordemRegistro)
            .ToList();
    }
}