# ConduitSharp.Transformation

Request/response transformation abstractions for ConduitSharp plugins.

Transform incoming requests or outgoing responses at the gateway:

- Header injection/removal
- Body rewriting (JSON, XML, etc.)
- Protocol upgrade/downgrade
- Custom serialization

Implement the transformation plugin interface to add your own logic.
