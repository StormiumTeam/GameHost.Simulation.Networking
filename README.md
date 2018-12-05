# package.stormiumteam.networking

### Use V2 Branch in the future instead: https://github.com/StormiumTeam/package.stormiumteam.networking/tree/v2_prototype

This is the original version of this networking library.
Even if it's more complete than the V2 version, the V1 branch is a complete mess.  
- Weird code
- Freeze when someone is connecting (creation of world per instance = system activator)
- Not compliant to ECS
- No threading possibility.
- Hard to update without breaking everything.
- A lot of allocations.
- Bad performance.
- And other things I forgot...
