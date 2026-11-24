using System;
using System.Collections.Generic;

// Resultado já pronto pra exibição — TokenStatBlockBuilder monta isso a partir de uma
// LocalFichaData (personagem) ou de um MonstroDado (monstro/inimigo), e o tooltip só exibe.
// [Serializable] adicionado pra permitir JsonUtility (ver SalaMobileBridge — manda isto pro celular
// quando ele pede o stat block de um token).
[Serializable]
public class TokenStatBlockData
{
    public string nome;
    public int vidaAtual;
    public int vidaMaxima;

    // Pronto pra exibir num único TMP_Text, ex: "22/33 8d10"
    public string vidaFormatada;

    // Já formatadas prontas pra exibir (sem o nome por extenso), ex: "Fur 6"
    public List<string> periciasTreinadas = new List<string>();

    // Ordem fixa: FOR, DES, CON, INT, SAB, CAR — já formatado, ex: "FOR 2", "DES -1"
    public string[] modificadoresAtributos = new string[6];

    public bool temCdMagico;
    public int cdMagico;

    // Ficha: ações/ataques da aba de combate. Monstro: as <action> do stat block.
    public List<AcaoStatBlock> acoesOuAtaques = new List<AcaoStatBlock>();

    // Só para fichas — magias preparadas (+ truques, sempre considerados preparados). Vazio pra monstro.
    public List<AcaoStatBlock> magiasPreparadas = new List<AcaoStatBlock>();

    // Só preenchidas no modo Testes (ver TokenStatBlockBuilder) — TODAS as perícias/salvaguardas,
    // treinadas ou não, cada uma com o modificador total já calculado e pronta pra rolar.
    public List<TesteStatBlock> periciasTestaveis = new List<TesteStatBlock>();
    public List<TesteStatBlock> salvaguardasTestaveis = new List<TesteStatBlock>();
}

// Forma da prévia de área desenhada no grid (ver FormaAreaGeometria) — Quadrado cobre cubo,
// Circulo cobre esfera/cilindro (mesma projeção 2D).
public enum FormaAreaAcao { Quadrado, Circulo, Cone, Linha }

// Uma linha clicável de ação/ataque/magia no tooltip.
[Serializable]
public class AcaoStatBlock
{
    public string nome;
    public string descricao; // notação de dano, ex: "1d8+3" — "-" se nada foi encontrado no texto original — USADA PRA ROLAR, não pra exibir descrição de verdade

    // Texto completo original (descrição de magia/item/ação, sem extração de dano) — usado pro
    // clique DIREITO (só mostrar descrição no chat, ver TokenStatBlockTooltipUI.OnClicarAcaoDetalhes).
    // Pode ficar vazio se a fonte de dados não tiver descrição (ex: ação de ficha sem "detalhes").
    public string descricaoCompleta;

    // Bônus de ataque (d20 + isto) pra rolagem via SelecaoAlvoManager — 0 = sem bônus conhecido
    // (ver TokenStatBlockBuilder: ficha usa AcaoDataTooltip.ataque, magia usa o bônus de ataque
    // mágico do personagem, monstro tenta extrair "+N para acertar" do texto livre).
    public int bonusAtaque;

    // Raio da prévia de área (AoE) em quadrados — 0 = sem área conhecida (ataque de alvo único).
    // Extraído via AlcanceExtrator (melhor esforço); ajustável manualmente durante a mira com
    // +/- (ver SelecaoAlvoManager) quando a extração falha ou erra.
    public int raioAreaQuadrados;

    // Alcance máximo do centro de origem em quadrados — 0 = desconhecido (não restringe cliques).
    public int alcanceQuadrados;

    // Forma da prévia de área (ver FormaAreaGeometria/FormaAreaExtrator) — default Quadrado.
    public FormaAreaAcao formaArea;

    // true = a área nasce SEMPRE na célula do token de origem (magias "Self" — jogador só escolhe
    // direção pra cone/linha); false = nasce na célula mirada pelo mouse (comportamento padrão).
    public bool origemNoToken;

    // Só preenchidos pra magia (ver TokenStatBlockBuilder.PreencherMagiasPreparadas) — texto pronto
    // pra exibir no chat de descrição (OnClicarAcaoDetalhes). Vazio = sem dado (arquivo de magia
    // ainda não tem os campos <casting-time>/<range>/<components>/<duration>, ou é ficha/monstro/item).
    public string tempoConjuracao;
    public string alcanceTexto;
    public string componentes;
    public string duracaoTexto;

    // true = ao confirmar o alvo, CADA token na área rola a PRÓPRIA resistência (ver
    // TesteResistenciaExtrator) contra cd — falha = dano cheio, sucesso = metade. false = ataque
    // (d20 + bonusAtaque, comportamento de antes desta fase). Detectado via texto livre, mesmo
    // "melhor esforço" do resto do sistema.
    public bool ehResistencia;

    // Nome canônico do atributo testado ("Destreza" etc, mesmo vocabulário de TesteStatBlock.nome)
    // — null se ehResistencia==false.
    public string atributoResistencia;

    // CD do teste de resistência — 0 = desconhecida (SelecaoAlvoManager cai pro comportamento de
    // ataque mesmo com ehResistencia==true, pra não travar sem CD nenhuma pra comparar).
    public int cd;
}

// Uma linha clicável de perícia/salvaguarda no modo Testes — clicar rola 1d20 + modificador (ver
// TokenStatBlockTooltipUI.OnClicarTeste).
[Serializable]
public class TesteStatBlock
{
    public string nome;
    public int modificador;
}
