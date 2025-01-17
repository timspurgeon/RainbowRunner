package config

import (
	"fmt"
	"github.com/spf13/viper"
)

var Config RRConfig

type LoggingOptions struct {
	LogMoves             bool            `mapstructure:"log_moves"`
	LogReceivedMoves     bool            `mapstructure:"log_received_moves"`
	LogGenericSent       bool            `mapstructure:"log_generic_sent"`
	LogChangeZone        bool            `mapstructure:"log_change_zone"`
	LogSmallAs           bool            `mapstructure:"log_small_as"`
	LogHashes            bool            `mapstructure:"log_hashes"`
	LogGCObjectSerialise bool            `mapstructure:"log_gc_object_serialise"`
	LogRandomEquipment   bool            `mapstructure:"log_random_equipment"`
	LogFilterMessages    bool            `mapstructure:"log_filter_messages"`
	LogSentMessageTypes  map[string]bool `mapstructure:"log_sent_message_types"`
	LogFileName          string          `mapstructure:"log_file_name"`
	LogTruncate          bool            `mapstructure:"log_truncate"`
	LogEMessages         bool            `mapstructure:"log_e_messages"`
	LogIDs               bool            `mapstructure:"log_ids"`
}

type NetworkOptions struct {
	LoginServerPort int    `mapstructure:"login_server_port"`
	GameServerPort  int    `mapstructure:"game_server_port"`
	GameServerIP    string `mapstructure:"game_server_ip"`
}

type WelcomeOptions struct {
	Message            string `mapstructure:"message"`
	SendWelcomeMessage bool   `mapstructure:"send_welcome_message"`
}

type RRConfig struct {
	Network                  NetworkOptions `mapstructure:"network"`
	SendMovementMessages     bool           `mapstructure:"send_movement_messages"`
	Logging                  LoggingOptions `mapstructure:"logging"`
	ReinitialiseZonesOnEnter bool           `mapstructure:"reinitialise_zones_on_enter"`
	Welcome                  WelcomeOptions `mapstructure:"welcome"`
	DefaultZone              string         `mapstructure:"default_zone"`
}

func Load() {
	viper.SetDefault("default_zone", "town")
	viper.SetDefault("network.login_server_port", 2110)
	viper.SetDefault("network.game_server_port", 2603)
	viper.SetDefault("network.game_server_ip", "127.0.0.1")

	viper.SetDefault("welcome.send_welcome_message", true)
	viper.SetDefault("welcome.message", `Welcome to RainbowRunner!
This server is currently in development and everything is broken. 
If you want to contribute to the codebase just head to https://github.com/EllieBelly4/RainbowRunner.`)

	viper.SetConfigName("config")                      // name of config file (without extension)
	viper.SetConfigType("yaml")                        // REQUIRED if the config file does not have the extension in the name
	viper.AddConfigPath("/etc/rainbowrunner/")         // path to look for the config file in
	viper.AddConfigPath("$HOME/.config/rainbowrunner") // call multiple times to add many search paths
	viper.AddConfigPath(".")                           // optionally look for config in the working directory
	err := viper.ReadInConfig()                        // Find and read the config file
	if err != nil {                                    // Handle errors reading the config file
		panic(fmt.Errorf("Fatal error config file: %w \n", err))
	}

	err = viper.Unmarshal(&Config)
	if err != nil {
		panic(fmt.Errorf("Fatal error config file: %w \n", err))
	}
}
