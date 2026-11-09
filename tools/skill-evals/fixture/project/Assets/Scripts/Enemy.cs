using UnityEngine;

public class Enemy : MonoBehaviour
{
    [SerializeField] float maxHealth = 100f;
    float health;

    void Awake() => health = maxHealth;

    public void TakeDamage(float damage)
    {
        health -= damage;
        if (health <= 0f)
            Destroy(gameObject);
    }
}
