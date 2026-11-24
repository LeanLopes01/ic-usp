using System.Collections.Generic;

/// <summary>Raiz do arquivo motor-de-dados.xml, na raiz do sistema (ver CarregadorMotorDeDados) —
/// tudo que a Forja de Sistemas autora pro sistema INTEIRO (diferente de schema_&lt;topico&gt;.xml,
/// que é por tópico de conteúdo): propriedades do personagem, tipos de rolagem configurados e ações
/// de teste.</summary>
public class DefinicaoMotorDeDados
{
    public List<DefinicaoPropriedade> propriedades = new List<DefinicaoPropriedade>();
    public Dictionary<string, DefinicaoTipoRolagem> tiposDeRolagem = new Dictionary<string, DefinicaoTipoRolagem>();
    public List<DefinicaoAcaoRolagem> acoes = new List<DefinicaoAcaoRolagem>();
}
