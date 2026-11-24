using System.Collections.Generic;

/// <summary>Uma ação de rolagem nomeada e testável na Forja (ex: "Ataque com Espada") — os valores
/// em parametros são TEXTO DE FÓRMULA (ex: parametros["modifier"] = "{mod_for}+{prof}"), resolvidos
/// contra o EstadoPersonagem real na hora de rolar (ver MotorDeDados.Rolar), nunca pré-calculados
/// aqui — assim um Modificador ativo (item equipado, magia) numa propriedade citada na fórmula entra
/// automaticamente na rolagem, sem nenhum código extra de sincronização.</summary>
public class DefinicaoAcaoRolagem
{
    public string id;
    public string nome;
    public string idTipoRolagem;
    public Dictionary<string, string> parametros = new Dictionary<string, string>();
}
