
```
v-- Our character, with his BulletCollider

[ \o/ ]
[ -|- ]             [o]**** <- Our projectile, with his BulletCollider
[ /\  ]
```

## On Collide
```
x0...1...2...3
[ \o/ ] /
[ -|- [o]****
[ /\  ] \
```

When colliding: 
Character BulletCollider invoke one event:  
`OnReceiveBullet      (BulletCollider offender,   BulletCollider.CollisionResultData result)`  
Projectile BulletCollider invoke one event:  
`OnBulletReceived  (BulletCollider victim,     BulletCollider.CollisionResultData result)`  
then Character invoke the events from the projectile, and the projectile invoke the event from the character.

```
result.WorldPosition = new Vector3(1.5f, ..., ...);
result.UId           = uid(characterCollider, projectileCollider);
result.Victim        = characterCollider;   // v
result.Offender      = projectileCollider;  // ^ As there is multiple events (atleast 2), Coll1 and Coll2 can be inversed. 
```