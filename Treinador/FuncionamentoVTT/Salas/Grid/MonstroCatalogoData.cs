using System;
using System.Collections.Generic;

[Serializable]
public class MonstroDado
{
    public string id;
    public string nome;
    public string meta;
    public List<string> categorias = new List<string>();
    public string ca;
    public string hp;
    public string velocidade;
    public string str, dex, con, intel, wis, cha;
    public string pericias;
    public string sentidos;
    public string idiomas;
    public string desafio;
    public List<MonstroAcao> acoes = new List<MonstroAcao>();
}

[Serializable]
public class MonstroAcao
{
    public string nome;
    public string descricao;
}
