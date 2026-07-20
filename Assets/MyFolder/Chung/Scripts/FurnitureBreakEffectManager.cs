using System.Collections.Generic;
using UnityEngine;

public class FurnitureBreakEffectManager : MonoBehaviour
{
    public static FurnitureBreakEffectManager Instance { get; private set; }

    [SerializeField] private FurnitureBreakEffect prefab;
    [SerializeField] private int poolSize = 8;

    private readonly List<FurnitureBreakEffect> _pool = new();

    private void Awake()
    {
        Instance = this;
        for (int i = 0; i < poolSize; i++)
        {
            var obj = Instantiate(prefab);
            obj.gameObject.SetActive(false);
            _pool.Add(obj);
        }
    }

    public void Spawn(Vector3 boxSize, Color color, Vector3 position, Vector3 hitNormal)
    {
        var effect = GetFromPool();
        effect.transform.position = position;
        effect.gameObject.SetActive(true);
        effect.Play(boxSize, color, hitNormal);
    }

    public void Return(FurnitureBreakEffect effect)
    {
        effect.gameObject.SetActive(false);
        _pool.Add(effect);
    }

    private FurnitureBreakEffect GetFromPool()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (!_pool[i].gameObject.activeSelf)
            {
                var effect = _pool[i];
                _pool.RemoveAt(i);
                return effect;
            }
        }
        // 풀 소진 시 새로 생성
        return Instantiate(prefab);
    }
}

