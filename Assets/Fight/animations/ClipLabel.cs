using UnityEngine;

public class ClipLabel : MonoBehaviour
{
    private Animator _anim;
    private TextMesh _text;

    private void Start()
    {
        _anim = GetComponent<Animator>();
        var go = new GameObject("ClipLabelText");
        go.transform.SetParent(Camera.main.transform, false);
        go.transform.localPosition = new Vector3(-0.55f, 0.35f, 1.5f);
        _text = go.AddComponent<TextMesh>();
        _text.fontSize = 64;
        _text.characterSize = 0.01f;
        _text.color = Color.white;
        _text.anchor = TextAnchor.UpperLeft;
    }

    private void Update()
    {
        var info = _anim.GetCurrentAnimatorClipInfo(0);
        if (info.Length > 0) _text.text = info[0].clip.name;
    }
}
