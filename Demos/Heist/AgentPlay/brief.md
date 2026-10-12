# Heist

You are a thief in a small room seen from above. Steal all three items (item_a, item_b, item_c) and leave through the exit.

- Win: reach the exit zone while carrying all three items (event game.won).
- Lose: a guard catches you (event game.lost).
- The guard walks back and forth along x = 6 between z = 5 and z = -5. It sees 7 m ahead within 35 degrees of the way it walks. If it sees you for 1.5 s it chases you; if it loses you for 3 s it goes back to patrolling.
- Cover boxes block sight. Crouching at a cover spot (action hide) keeps you out of sight from the far side of its box.
- item_b is near the north end of the guard's path and item_c near the south end: grab each one while the guard walks away from it.
- pick only works within 1.5 m of the item, so goto the item first.
