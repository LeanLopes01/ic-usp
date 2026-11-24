using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class AjusteClimaCamera : MonoBehaviour
{
    private ParticleSystem sistemaParticulas;
    private Camera cam;

    void Start()
    {
        sistemaParticulas = GetComponent<ParticleSystem>();
        // Garante que está pegando a câmera onde o efeito é filho
        cam = GetComponentInParent<Camera>(); 
        
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        if (cam == null || !cam.orthographic) return;

        // A altura da visão ortográfica é o orthographicSize vezes 2
        float altura = cam.orthographicSize * 2f;
        // A largura é a altura multiplicada pela proporção da tela (aspect ratio)
        float largura = altura * cam.aspect;

        // Acessa o módulo Shape e ajusta a escala 
        // (+2f é uma folga para a chuva nascer um pouco fora da tela e não brotar do nada)
        var shape = sistemaParticulas.shape;
        shape.scale = new Vector3(largura + 2f, altura + 2f, 1f);
    }
}