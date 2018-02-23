FROM ubuntu:17.10
LABEL maintainer="lucasontivero@gmail.com"
RUN apt-get update
RUN apt-get install --yes software-properties-common
RUN add-apt-repository --yes ppa:bitcoin/bitcoin
RUN apt-get update

# install bitcoind (from PPA) and make
RUN apt-get install --yes bitcoind

# copy bitcoin.conf
ADD . /home/bitcoind-regtest
    
# WORKDIR sets the working directory for any RUN, CMD, ENTRYPOINT, COPY and ADD instructions that follow
WORKDIR /home/bitcoind-regtest

# Expose RPC port and Peers port
EXPOSE 8332
EXPOSE 18332

# Add a function for callin bitcoin-cli
RUN echo bcli\(\){ bitcoin-cli -datadir=/home/bitcoind-regtest/node \$1\; }>>~/.bashrc
RUN echo export -f bcli>>~/.bashrc

# There can only be one CMD instruction in a Dockerfile
# CMD provides defaults for an executing container
# Drop user into bash shell by default
CMD ["/bin/bash"]

