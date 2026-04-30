namespace Mikk.Avatar
{
    public enum GestureHint
    {
        None = 0,           // Pure VAD-driven procedural

        // Conversational
        Greeting,           // "aur kaise ho", "namaste", "hey what's up"
        Question,           // "kya lagta hai?", "how does this work?"
        Affirmation,        // "haan bilkul", "exactly", "sahi kaha"
        Negation,           // "nahi nahi", "that's wrong", "bilkul nahi"

        // Expressive
        Emphasis,           // "bahut important hai", "seriously listen"
        Uncertainty,        // "pata nahi", "maybe", "I'm not sure"
        Calming,            // "tension mat le", "relax", "sab theek hoga"
        Pointing,           // "ye dekho", "look at this", "wahan dekh"

        // Structural
        Listing,            // "pehle ye, phir wo", "first... second..."
        Offering,           // "ye lo", "try this", "here take it"
        Dismissal,          // "chodo", "forget it", "doesn't matter"
        Explaining,         // "matlab ye ki", "basically", "samjho"
        Celebrating,        // "let's go!", "we did it!", "party time"
        Requesting,         // "please help", "ek kaam karo", "zara suno"
        Storytelling,       // "ek baar ki baat hai", "so what happened was"
        Thinking,           // "hmm let me think", "ruko sochta hu"
    }
}