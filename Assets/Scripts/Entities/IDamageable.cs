using UnityEngine;

public interface IDamageable
{
    void TakeDamage(int damage, Vector3 dashDirection);
    bool IsAlive { get; }
    bool IsImpassable { get; }
}
