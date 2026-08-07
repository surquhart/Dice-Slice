public interface IMovementBehavior
{
    void Initialize(DamageableEntity owner);
    void Move(float deltaTime);
}
