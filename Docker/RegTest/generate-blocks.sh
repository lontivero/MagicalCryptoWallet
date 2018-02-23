# Start the bitcoin node
bitcoind -datadir=node -daemon

# Generate 120 blocks
bitcoin-cli -regtest generate 150

for block in {1..20}; do
    for i in {1..100}; do 
        # Get a new receive address.
        ADDRESS=$(bitcoin-cli -regtest getnewaddress)
        #WIT_ADDRESS=$(bitcoin-cli -regtest addwitnessaddress $ADDRESS)
        TXID=$(bitcoin-cli -regtest sendtoaddress $ADDRESS 10)
        echo 1 BTC sent to $ADDRESS txid $TXID
    done
    bitcoin-cli -regtest generate 1
done

