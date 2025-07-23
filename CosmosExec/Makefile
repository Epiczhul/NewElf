CC = gcc
CFLAGS = -m32 -nostdlib -ffreestanding -O0 -g -Wno-discarded-qualifiers -Wno-attributes -I./include
LDFLAGS = -Wl,--build-id=none -Wl,-e,_start -O0 -g -I./include
OUTPUT = TestElf

SOURCES = $(wildcard *.c)
OBJECTS = $(SOURCES:.c=.o)

all: $(OUTPUT)

$(OUTPUT): $(SOURCES)
	$(CC) $(CFLAGS) $(LDFLAGS) $(SOURCES) -o $(OUTPUT)

build: $(OUTPUT)
	cp $(OUTPUT) ../RawExecution/Drivers/ELF/$(OUTPUT)

clean:
	rm -f $(OUTPUT) $(OBJECTS)