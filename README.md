# SpineTools-CSharp
NHS Spine Message Handler

Dependencies:

.Net 4.0 or 4.5

DistributionEnvelopeTools-CSharp (https://github.com/DamianJMurphy/DistributionEnvelopeTools-CSharp)

JSON.Net (http://www.newtonsoft.com/json)

SpineTools is an integration library providing Message Handling System behaviour for NHS Spine clients, and in particular for "ITK Trunk" messages. It supports synchronous  and unreliable asynchronous queries;  and asychronous messaging  with both synchronous and asychnonous ebXML acknowledgments. 

SpineTools provides a Spine Directory Service interface  plus a cache .

Integration points for received messages are provided by handlers keyed on service, with default (or example) handlers being provided that write received message content to files on disk. Configuration is via the registry.

API documentation is in "Javadoc" form and  Doxygen (http://www.doxygen.org) is recommended to extract and format it.
