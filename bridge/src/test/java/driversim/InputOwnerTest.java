package driversim;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;

import org.junit.jupiter.api.Test;

class InputOwnerTest {
    @Test
    void parsesWireNames() {
        assertEquals(InputOwner.GAME, InputOwner.parse("game"));
        assertEquals(InputOwner.GUI, InputOwner.parse("gui"));
        assertEquals(InputOwner.SELFTEST, InputOwner.parse(" selftest "));
        assertEquals("selftest", InputOwner.SELFTEST.getWireName());
    }

    @Test
    void returnsNullForMissingOrUnknown() {
        assertNull(InputOwner.parse(null));
        assertNull(InputOwner.parse(""));
        assertNull(InputOwner.parse("keyboard"));
    }
}
